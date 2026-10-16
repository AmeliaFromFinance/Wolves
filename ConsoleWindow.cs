using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wolves;

/// <summary>
/// Sets the console window title and icon. The exe icon (ApplicationIcon) is only used by Explorer;
/// the console window belongs to conhost and shows its own icon unless we replace it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ConsoleWindow
{
    private const uint WmSetIcon = 0x0080;
    private static readonly IntPtr IconSmall = 0;
    private static readonly IntPtr IconBig = 1;

    public static void Apply()
    {
        try
        {
            Console.Title = $"{AppInfo.Name} v{AppInfo.Version}";
        }
        catch (IOException) { /* no console attached */ }

        try
        {
            IntPtr window = GetConsoleWindow();
            string? exePath = Environment.ProcessPath;
            if (window == IntPtr.Zero || exePath is null)
                return;

            // Take the icon embedded in Wolves.exe so there is a single source of truth
            if (ExtractIconEx(exePath, 0, out IntPtr large, out IntPtr small, 1) == 0)
                return;

            if (large != IntPtr.Zero)
                SendMessage(window, WmSetIcon, IconBig, large);
            if (small != IntPtr.Zero)
                SendMessage(window, WmSetIcon, IconSmall, small);
        }
        catch (Exception) { /* cosmetic only */ }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);
}
