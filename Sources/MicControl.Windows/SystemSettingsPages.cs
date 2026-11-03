using System.ComponentModel;
using System.Diagnostics;
using MicControl.Platform;

namespace MicControl.Windows;

public sealed class SystemSettingsPages : ISystemSettingsPages
{
    /// <summary>
    /// Neither Windows 10 nor 11 links straight to the tray icon list, so the steps name where it is in Settings →
    /// Personalization → Taskbar: a link on 10, a collapsed section on 11 (where the Microphone icon may not be listed).
    /// </summary>
    public string TrayIconSteps { get; } = Environment.OSVersion.Version.Build >= 22000
        ? "Opens Taskbar settings. Scroll down and open “Other system tray icons”, then turn on MicControl."
        : "Opens Taskbar settings. Under “Notification area”, choose “Select which icons appear on the taskbar”. Turn on MicControl and turn off Microphone.";

    public bool OpenTrayIcons()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
