using System.ComponentModel;
using System.Diagnostics;
using MicControl.Platform;

namespace MicControl.Windows;

public sealed class SystemSettingsPages : ISystemSettingsPages
{
    /// <summary>
    /// Windows 10's "Notification Area Icons" (a Show/Hide choice for every icon, the Microphone one included);
    /// Windows 11 dropped it, and its Taskbar settings list the tray icons instead.
    /// </summary>
    private static string TrayIconsPage => Environment.OSVersion.Version.Build >= 22000
        ? "ms-settings:taskbar"
        : "shell:::{05D7B0F4-2121-4EFF-BF6B-ED3F69B894D9}";

    public bool OpenTrayIcons()
    {
        try
        {
            Process.Start(new ProcessStartInfo(TrayIconsPage) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
