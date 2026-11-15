#define NOMINMAX
#include <windows.h>
#include <dwmapi.h>
#include <DispatcherQueue.h>
#include <windows.ui.composition.interop.h>
#include <windows.graphics.effects.interop.h>
#include <d2d1effects.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <memory>
#include <vector>
#include <cmath>
#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <functiondiscoverykeys_devpkey.h>

using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Graphics::Effects;
using namespace Windows::UI::Composition;
using namespace Windows::UI::Composition::Desktop;
namespace EffectsABI=ABI::Windows::Graphics::Effects;

struct GaussianEffect:implements<GaussianEffect,IGraphicsEffect,IGraphicsEffectSource,EffectsABI::IGraphicsEffectD2D1Interop>
{
    hstring name{L"Blur"}; IGraphicsEffectSource source{nullptr};
    hstring Name(){return name;} void Name(hstring const& value){name=value;}
    HRESULT __stdcall GetEffectId(GUID* id) noexcept override{*id=CLSID_D2D1GaussianBlur;return S_OK;}
    HRESULT __stdcall GetNamedPropertyMapping(LPCWSTR key,UINT* index,EffectsABI::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept override
    {if(wcscmp(key,L"BlurAmount")!=0)return E_INVALIDARG;*index=0;*mapping=EffectsABI::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;return S_OK;}
    HRESULT __stdcall GetPropertyCount(UINT* count) noexcept override{*count=3;return S_OK;}
    HRESULT __stdcall GetProperty(UINT index,ABI::Windows::Foundation::IPropertyValue** value) noexcept override
    {
        try{
            Windows::Foundation::IInspectable boxed{nullptr};
            if(index==0)boxed=PropertyValue::CreateSingle(16.f);
            else if(index==1)boxed=PropertyValue::CreateUInt32(1); // balanced
            else if(index==2)boxed=PropertyValue::CreateUInt32(1); // hard edge
            else return E_INVALIDARG;
            *value=reinterpret_cast<ABI::Windows::Foundation::IPropertyValue*>(detach_abi(boxed.as<IPropertyValue>()));return S_OK;
        }catch(...){return to_hresult();}
    }
    HRESULT __stdcall GetSource(UINT index,EffectsABI::IGraphicsEffectSource** value) noexcept override
    {if(index!=0)return E_INVALIDARG;auto copy=source;*value=reinterpret_cast<EffectsABI::IGraphicsEffectSource*>(detach_abi(copy));return S_OK;}
    HRESULT __stdcall GetSourceCount(UINT* count) noexcept override{*count=1;return S_OK;}
};

struct CompositionServices
{
    Windows::System::DispatcherQueueController queue{nullptr};
    Compositor compositor{nullptr};
};
struct Backdrop
{
    HWND owner,hwnd{};Compositor compositor{nullptr};DesktopWindowTarget target{nullptr};SpriteVisual sprite{nullptr};CompositionEffectBrush brush{nullptr};CompositionRoundedRectangleGeometry rounded{nullptr};
    bool regional=false;float regionX{},regionY{},regionWidth{},regionHeight{},cornerRadius=8.f;
    Backdrop(HWND window):owner(window)
    {
        // These two bounded UI-thread services live until process termination.
        // C++ thread_local WinRT destructors run after CLR apartment teardown,
        // which can fail-fast inside CoreUIComponents during a normal exit.
        static thread_local auto services=new CompositionServices;
        if(!Windows::System::DispatcherQueue::GetForCurrentThread()){
            DispatcherQueueOptions options{sizeof(DispatcherQueueOptions),DQTYPE_THREAD_CURRENT,DQTAT_COM_STA};
            check_hresult(CreateDispatcherQueueController(options,reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(services->queue))));
        }
        // Keep one compositor alive for the UI thread. Transient popup targets
        // must not repeatedly tear down the thread's composition infrastructure.
        if(!services->compositor)services->compositor=Compositor();
        compositor=services->compositor;
        static ATOM atom=[](){WNDCLASSW wc{};wc.lpfnWndProc=[](HWND h,UINT m,WPARAM w,LPARAM l)->LRESULT{
            if(m==WM_NCCREATE){auto create=reinterpret_cast<CREATESTRUCTW*>(l);SetWindowLongPtrW(h,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(create->lpCreateParams));}
            auto state=reinterpret_cast<Backdrop*>(GetWindowLongPtrW(h,GWLP_USERDATA));
            if(m==WM_TIMER&&state&&(!IsWindow(state->owner)||!IsWindowVisible(state->owner)||IsIconic(state->owner)))state->Hide();
            if(m==WM_NCCALCSIZE)return 0;if(m==WM_NCHITTEST)return HTTRANSPARENT;if(m==WM_MOUSEACTIVATE)return MA_NOACTIVATE;return DefWindowProcW(h,m,w,l);
        };wc.hInstance=GetModuleHandleW(nullptr);wc.lpszClassName=L"NewStart.CustomBackdrop";return RegisterClassW(&wc);}();
        hwnd=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT,L"NewStart.CustomBackdrop",L"",WS_POPUP|WS_CAPTION,0,0,1,1,nullptr,nullptr,GetModuleHandleW(nullptr),this);
        if(!hwnd)throw_last_error();
        MARGINS margins{-1,-1,-1,-1};check_hresult(DwmExtendFrameIntoClientArea(hwnd,&margins));
        int corner=2;DwmSetWindowAttribute(hwnd,DWMWA_WINDOW_CORNER_PREFERENCE,&corner,sizeof(corner));
        auto interop=compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
        check_hresult(interop->CreateDesktopWindowTarget(hwnd,FALSE,reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
        auto effect=make_self<GaussianEffect>();effect->source=CompositionEffectSourceParameter(L"backdrop");
        auto factory=compositor.CreateEffectFactory(effect.as<IGraphicsEffect>(),std::vector<hstring>{L"Blur.BlurAmount"});
        brush=factory.CreateBrush();brush.SetSourceParameter(L"backdrop",compositor.CreateHostBackdropBrush());
        sprite=compositor.CreateSpriteVisual();sprite.Brush(brush);rounded=compositor.CreateRoundedRectangleGeometry();sprite.Clip(compositor.CreateGeometricClip(rounded));target.Root(sprite);SetTimer(hwnd,1,100,nullptr);
    }
    ~Backdrop(){if(hwnd){KillTimer(hwnd,1);ShowWindow(hwnd,SW_HIDE);}if(target){target.Root(nullptr);target.Close();target=nullptr;}sprite=nullptr;rounded=nullptr;brush=nullptr;if(hwnd){SetWindowLongPtrW(hwnd,GWLP_USERDATA,0);DestroyWindow(hwnd);}}
    void Hide() noexcept{ShowWindow(hwnd,SW_HIDE);try{if(target&&target.Root())target.Root(nullptr);}catch(...){}}
    void Update(float radius,bool visible)
    {
        // At zero, remove the entire host backdrop: its built-in smoothing must not remain.
        if(radius<=0.f||!visible||!IsWindow(owner)||IsIconic(owner)){Hide();return;}
        if(!target.Root())target.Root(sprite);
        BOOL enabled=TRUE;check_hresult(DwmSetWindowAttribute(hwnd,DWMWA_USE_HOSTBACKDROPBRUSH,&enabled,sizeof(enabled)));
        int none=1;check_hresult(DwmSetWindowAttribute(hwnd,DWMWA_SYSTEMBACKDROP_TYPE,&none,sizeof(none)));
        RECT placement{};GetWindowRect(owner,&placement);
        float scale=GetDpiForWindow(owner)/96.f;
        if(regional){placement.left+=LONG(std::lround(regionX*scale));placement.top+=LONG(std::lround(regionY*scale));placement.right=placement.left+LONG(std::lround(regionWidth*scale));placement.bottom=placement.top+LONG(std::lround(regionHeight*scale));}
        SetWindowPos(hwnd,owner,placement.left,placement.top,placement.right-placement.left,placement.bottom-placement.top,SWP_NOACTIVATE|SWP_NOOWNERZORDER|SWP_SHOWWINDOW);
        RECT rect{};GetClientRect(hwnd,&rect);
        Windows::Foundation::Numerics::float2 size{float(rect.right),float(rect.bottom)};sprite.Size(size);rounded.Size(size);rounded.CornerRadius({cornerRadius*scale,cornerRadius*scale});
        brush.Properties().InsertScalar(L"Blur.BlurAmount",radius*scale);
    }
};
extern "C" __declspec(dllexport) HRESULT __stdcall CreateBackdrop(HWND hwnd,float radius,BOOL visible,void** result) noexcept
{try{auto state=std::make_unique<Backdrop>(hwnd);state->Update(radius,visible!=FALSE);*result=state.release();return S_OK;}catch(...){*result=nullptr;return to_hresult();}}
extern "C" __declspec(dllexport) HRESULT __stdcall UpdateBackdrop(void* state,float radius,BOOL visible) noexcept
{try{static_cast<Backdrop*>(state)->Update(radius,visible!=FALSE);return S_OK;}catch(...){return to_hresult();}}
extern "C" __declspec(dllexport) BOOL __stdcall IsBackdropVisible(void* state) noexcept{return state&&IsWindowVisible(static_cast<Backdrop*>(state)->hwnd);}
extern "C" __declspec(dllexport) HRESULT __stdcall UpdateBackdropRegion(void* state,float x,float y,float width,float height,float corner,float radius,BOOL visible) noexcept
{try{auto backdrop=static_cast<Backdrop*>(state);backdrop->regional=true;backdrop->regionX=x;backdrop->regionY=y;backdrop->regionWidth=width;backdrop->regionHeight=height;backdrop->cornerRadius=corner;backdrop->Update(radius,visible!=FALSE&&width>0&&height>0);return S_OK;}catch(...){return to_hresult();}}
extern "C" __declspec(dllexport) HRESULT __stdcall RebindBackdrop(void* state,HWND owner,float radius,BOOL visible) noexcept
{try{auto backdrop=static_cast<Backdrop*>(state);backdrop->owner=owner;backdrop->Update(radius,visible!=FALSE);return S_OK;}catch(...){return to_hresult();}}
extern "C" __declspec(dllexport) void __stdcall DestroyBackdrop(void* state) noexcept{delete static_cast<Backdrop*>(state);}

#include "Audio.cpp"
