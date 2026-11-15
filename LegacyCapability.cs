using System;
using System.Runtime.InteropServices;

namespace NewStart;
internal static class LegacyCapability
{
    [DllImport("oleacc.dll")]static extern int AccessibleObjectFromWindow(IntPtr window,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object accessible);
    static bool Access(IntPtr window,string name,bool invoke)
    {
        if(window==IntPtr.Zero)return false;object? value=null;
        try
        {
            var iid=new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");if(AccessibleObjectFromWindow(window,0xFFFFFFFC,ref iid,out value)!=0)return false;
            var accessible=(Accessibility.IAccessible)value;
            if(accessible.get_accName(0)!=name||string.IsNullOrWhiteSpace(accessible.get_accDefaultAction(0)))return false;
            int state=Convert.ToInt32(accessible.get_accState(0));if((state&0x8001)!=0)return false;
            if(invoke)accessible.accDoDefaultAction(0);return true;
        }catch{return false;}finally{if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    }
    internal static bool CanInvoke(IntPtr window,string name)=>Access(window,name,false);
    internal static bool Invoke(IntPtr window,string name)=>Access(window,name,true);
}
