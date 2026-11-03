using System.ComponentModel;
using System.Diagnostics;
using MicControl.Platform;

namespace MicControl.Windows;

public sealed class SystemSettingsPages : ISystemSettingsPages
{
    /// <summary>
    /// Settings → Personalization → Taskbar. Neither Windows 10 nor 11 has a link straight to the tray icon list, so
    /// the steps name the section to open: a link on 10, a collapsed section on 11.
    /// </summary>
    public string? OpenTrayIcons()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            return null;
        }

        var section = Environment.OSVersion.Version.Build >= 22000
            ? "open \"Other system tray icons\""
            : "click \"Select which icons appear on the taskbar\"";
        return $"In the taskbar settings, {section}, then turn MicControl on and Microphone off.";
    }
}
