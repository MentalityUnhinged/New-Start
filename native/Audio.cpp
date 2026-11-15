#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <functiondiscoverykeys_devpkey.h>
#include <atomic>
#include <string>

using AudioChanged=void(__stdcall*)();
// Optional compatibility adapter. Windows has no public default-endpoint setter.
// Keep the private ABI isolated; COM/HRESULT failure leaves the UI on actual state.
struct __declspec(uuid("f8679f50-850a-41cf-9c72-430f290290c8")) OutputPolicy: ::IUnknown
{
    virtual HRESULT STDMETHODCALLTYPE GetMixFormat(LPCWSTR,void**)=0;
    virtual HRESULT STDMETHODCALLTYPE GetDeviceFormat(LPCWSTR,BOOL,void**)=0;
    virtual HRESULT STDMETHODCALLTYPE ResetDeviceFormat(LPCWSTR)=0;
    virtual HRESULT STDMETHODCALLTYPE SetDeviceFormat(LPCWSTR,void*,void*)=0;
    virtual HRESULT STDMETHODCALLTYPE GetProcessingPeriod(LPCWSTR,BOOL,void*,void*)=0;
    virtual HRESULT STDMETHODCALLTYPE SetProcessingPeriod(LPCWSTR,void*)=0;
    virtual HRESULT STDMETHODCALLTYPE GetShareMode(LPCWSTR,void*)=0;
    virtual HRESULT STDMETHODCALLTYPE SetShareMode(LPCWSTR,void*)=0;
    virtual HRESULT STDMETHODCALLTYPE GetPropertyValue(LPCWSTR,const PROPERTYKEY&,PROPVARIANT*)=0;
    virtual HRESULT STDMETHODCALLTYPE SetPropertyValue(LPCWSTR,const PROPERTYKEY&,PROPVARIANT*)=0;
    virtual HRESULT STDMETHODCALLTYPE SetDefaultEndpoint(LPCWSTR,ERole)=0;
    virtual HRESULT STDMETHODCALLTYPE SetEndpointVisibility(LPCWSTR,BOOL)=0;
};
struct AudioService final:IMMNotificationClient,IAudioEndpointVolumeCallback
{
    std::atomic<ULONG> refs{1};std::atomic<AudioChanged> callback{};
    winrt::com_ptr<IMMDeviceEnumerator> enumerator;winrt::com_ptr<IAudioEndpointVolume> endpoint;std::wstring id;
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{auto n=--refs;if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** value) override
    {if(!value)return E_POINTER;*value=nullptr;if(iid==__uuidof(::IUnknown)||iid==__uuidof(IMMNotificationClient))*value=static_cast<IMMNotificationClient*>(this);else if(iid==__uuidof(IAudioEndpointVolumeCallback))*value=static_cast<IAudioEndpointVolumeCallback*>(this);else return E_NOINTERFACE;AddRef();return S_OK;}
    void Notify(){if(auto fn=callback.load())fn();}
    HRESULT STDMETHODCALLTYPE OnNotify(PAUDIO_VOLUME_NOTIFICATION_DATA) override{Notify();return S_OK;}
    HRESULT STDMETHODCALLTYPE OnDeviceStateChanged(LPCWSTR,DWORD) override{Notify();return S_OK;}
    HRESULT STDMETHODCALLTYPE OnDeviceAdded(LPCWSTR) override{Notify();return S_OK;}
    HRESULT STDMETHODCALLTYPE OnDeviceRemoved(LPCWSTR) override{Notify();return S_OK;}
    HRESULT STDMETHODCALLTYPE OnDefaultDeviceChanged(EDataFlow flow,ERole role,LPCWSTR) override{if(flow==eRender&&role==eMultimedia)Notify();return S_OK;}
    HRESULT STDMETHODCALLTYPE OnPropertyValueChanged(LPCWSTR,const PROPERTYKEY) override{Notify();return S_OK;}
    void Bind()
    {
        winrt::com_ptr<IMMDevice> device;winrt::check_hresult(enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,device.put()));
        LPWSTR next{};winrt::check_hresult(device->GetId(&next));std::wstring current(next);CoTaskMemFree(next);
        if(current==id&&endpoint)return;
        if(endpoint){endpoint->UnregisterControlChangeNotify(this);endpoint=nullptr;}id.clear();
        winrt::check_hresult(device->Activate(__uuidof(IAudioEndpointVolume),CLSCTX_ALL,nullptr,endpoint.put_void()));
        winrt::check_hresult(endpoint->RegisterControlChangeNotify(this));id=current;
    }
    void Stop(){callback=nullptr;if(endpoint){endpoint->UnregisterControlChangeNotify(this);endpoint=nullptr;}if(enumerator){enumerator->UnregisterEndpointNotificationCallback(this);enumerator=nullptr;}}
};
static std::wstring AudioName(IMMDevice* device)
{
    winrt::com_ptr<IPropertyStore> properties;winrt::check_hresult(device->OpenPropertyStore(STGM_READ,properties.put()));
    PROPVARIANT value{};winrt::check_hresult(properties->GetValue(PKEY_Device_FriendlyName,&value));std::wstring name=value.vt==VT_LPWSTR?value.pwszVal:L"Audio output";PropVariantClear(&value);return name;
}
extern "C" __declspec(dllexport) HRESULT __stdcall AudioCreate(AudioChanged callback,void** result) noexcept
{
    auto state=new(std::nothrow) AudioService;if(!state)return E_OUTOFMEMORY;
    try{winrt::check_hresult(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,__uuidof(IMMDeviceEnumerator),state->enumerator.put_void()));state->callback=callback;winrt::check_hresult(state->enumerator->RegisterEndpointNotificationCallback(state));*result=state;return S_OK;}
    catch(...){state->Stop();state->Release();return winrt::to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall AudioRead(void* value,float* volume,BOOL* muted,wchar_t* name,int capacity,wchar_t* outputs,int outputCapacity) noexcept
{
    try{auto state=static_cast<AudioService*>(value);state->Bind();winrt::check_hresult(state->endpoint->GetMasterVolumeLevelScalar(volume));winrt::check_hresult(state->endpoint->GetMute(muted));
        winrt::com_ptr<IMMDevice> current;winrt::check_hresult(state->enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,current.put()));wcsncpy_s(name,capacity,AudioName(current.get()).c_str(),_TRUNCATE);
        winrt::com_ptr<IMMDeviceCollection> devices;winrt::check_hresult(state->enumerator->EnumAudioEndpoints(eRender,DEVICE_STATE_ACTIVE,devices.put()));UINT count{};devices->GetCount(&count);std::wstring names;
        for(UINT i=0;i<count;i++){winrt::com_ptr<IMMDevice> device;if(SUCCEEDED(devices->Item(i,device.put()))){LPWSTR id{};if(FAILED(device->GetId(&id)))continue;if(!names.empty())names+=L"\n";names+=AudioName(device.get())+L"\t"+id;CoTaskMemFree(id);}}
        wcsncpy_s(outputs,outputCapacity,names.c_str(),_TRUNCATE);return S_OK;
    }catch(...){return winrt::to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall AudioWrite(void* value,float volume,BOOL muted) noexcept
{try{auto state=static_cast<AudioService*>(value);state->Bind();GUID context{};winrt::check_hresult(state->endpoint->SetMasterVolumeLevelScalar(volume,&context));winrt::check_hresult(state->endpoint->SetMute(muted,&context));return S_OK;}catch(...){return winrt::to_hresult();}}
extern "C" __declspec(dllexport) void __stdcall AudioDestroy(void* value) noexcept{auto state=static_cast<AudioService*>(value);state->Stop();state->Release();}
extern "C" __declspec(dllexport) HRESULT __stdcall AudioSelectOutput(void* value,LPCWSTR id) noexcept
{
    try
    {
        auto state=static_cast<AudioService*>(value);winrt::com_ptr<IMMDevice> device;winrt::check_hresult(state->enumerator->GetDevice(id,device.put()));DWORD status{};winrt::check_hresult(device->GetState(&status));if(status!=DEVICE_STATE_ACTIVE)return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        CLSID clsid{0x870af99c,0x171d,0x4f9e,{0xaf,0x0d,0xe6,0x3d,0xf4,0x0c,0x2b,0xc9}};winrt::com_ptr<OutputPolicy> policy;
        winrt::check_hresult(CoCreateInstance(clsid,nullptr,CLSCTX_ALL,__uuidof(OutputPolicy),policy.put_void()));
        winrt::check_hresult(policy->SetDefaultEndpoint(id,eConsole));winrt::check_hresult(policy->SetDefaultEndpoint(id,eMultimedia));
        winrt::com_ptr<IMMDevice> actual;winrt::check_hresult(state->enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,actual.put()));LPWSTR current{};winrt::check_hresult(actual->GetId(&current));bool matches=wcscmp(id,current)==0;CoTaskMemFree(current);return matches?S_OK:E_FAIL;
    }catch(...){return winrt::to_hresult();}
}
