using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NewStart;
internal static class SurfaceAppearance
{
    internal static bool TryHex(string? value,out Color colour)
    {
        colour=Colors.Black;string text=(value??"").Trim().TrimStart('#');
        if(text.Length!=6||!uint.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint number))return false;
        colour=Color.FromRgb((byte)(number>>16),(byte)(number>>8),(byte)number);return true;
    }
    internal static string Hex(Color colour)=>$"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
    internal static string HexOrDefault(string? value)=>TryHex(value,out var colour)?Hex(colour):"#000000";
    internal static double Transparency(Preferences prefs,bool appBar)=>Math.Clamp((appBar?prefs.AppBarTransparency:prefs.DockTransparency)??prefs.StartTransparency,0,1);
    internal static double Blur(Preferences prefs,bool appBar)=>Math.Clamp((appBar?prefs.AppBarBlur:prefs.DockBlur)??prefs.MenuBlur,0,60);
    internal static Color Background(Preferences prefs,bool appBar,string? monitor=null)
    {
        int source=appBar?prefs.AppBarColourSource:prefs.DockColourSource;
        if(source==0)return Appearance.IsDark(prefs.Theme)?Colors.Black:Colors.White;
        if(source==2)
        {
            var palette=WallpaperPalette.Current?.ForMonitor(monitor);
            if(palette is {Colours.Length:>0})
            {
                var colour=(appBar?prefs.AppBarWallpaperStrategy:prefs.DockWallpaperStrategy)==0?palette.Dominant:palette.Accent;
                if(appBar)prefs.AppBarWallpaperFallback=Hex(colour);else prefs.DockWallpaperFallback=Hex(colour);return colour;
            }
            TryHex(appBar?prefs.AppBarWallpaperFallback:prefs.DockWallpaperFallback,out var fallback);return fallback;
        }
        TryHex(appBar?prefs.AppBarColour:prefs.DockColour,out var custom);return custom;
    }
    internal static Color Composite(Color top,Color bottom,double transparency)=>Color.FromRgb((byte)Math.Round(top.R*(1-transparency)+bottom.R*transparency),(byte)Math.Round(top.G*(1-transparency)+bottom.G*transparency),(byte)Math.Round(top.B*(1-transparency)+bottom.B*transparency));
    internal static double Luminance(Color colour)
    {
        static double Channel(byte b){double c=b/255d;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}
        return .2126*Channel(colour.R)+.7152*Channel(colour.G)+.0722*Channel(colour.B);
    }
    internal static bool WhiteInk(Color colour)=>Luminance(colour)<.179;
    internal static Color Effective(Preferences prefs,bool appBar,string? monitor=null)
    {
        var wallpaper=WallpaperPalette.Current?.ForMonitor(monitor)?.Dominant??(Appearance.IsDark(prefs.Theme)?Colors.Black:Colors.White);
        return Composite(Background(prefs,appBar,monitor),wallpaper,Transparency(prefs,appBar));
    }
    internal static Brush Ink(Preferences prefs,bool appBar,string? monitor=null)=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:WhiteInk(Effective(prefs,appBar,monitor))?Brushes.White:Brushes.Black;
    internal static (Brush Tint,Brush Rim) Material(Color colour,double transparency)
    {
        double opacity=1-Math.Clamp(transparency,0,1);
        var fill=new SolidColorBrush(colour){Opacity=opacity};fill.Freeze();
        // No opaque outline at 100%; at 0% the selected solid colour covers every surface pixel.
        return(fill,Brushes.Transparent);
    }
}
