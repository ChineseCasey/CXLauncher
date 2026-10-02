using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CXLauncher.Views;

internal static class NativeWindow
{
    public static void UseDarkCaption(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        var captionColor = 0x00181311;
        _ = DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
