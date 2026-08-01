using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LogViewer.App.Services;

/// <summary>
/// Toggles the DWM "immersive dark mode" title bar for a window. WPF's Fluent <c>ThemeMode</c> repaints
/// everything inside the client area but never the OS-drawn title bar/min-max-close buttons — this is
/// the separate, documented Win32 call needed to match it, so a dark theme doesn't leave a light strip
/// across the top of every window.
/// </summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    public static void Apply(Window window, bool isDark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }
}
