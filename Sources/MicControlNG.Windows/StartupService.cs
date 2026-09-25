using System.Diagnostics;
using System.Security.Principal;
using MicControlNG.Platform;
using Microsoft.Win32;

namespace MicControlNG.Windows;

/// <summary>
/// Registers MicControlNG to start at sign-in. Elevated processes are blocked from the Run key at logon,
/// so when running elevated a scheduled task with highest privileges is used instead.
/// </summary>
public sealed class StartupService : IStartupRegistration
{
    public const string AutostartArgument = "--autostart";
    private const string Name = "MicControlNG";
    private const string LegacyName = "MicSwitch";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string Command => $"\"{Environment.ProcessPath}\" {AutostartArgument}";

    public bool IsEnabled => HasRunKey(Name) || HasScheduledTask(Name);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (IsElevated)
            {
                var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
                Schtasks($"/Create /F /TN \"{Name}\" /TR \"{Command.Replace("\"", "\\\"", StringComparison.Ordinal)}\" /SC ONLOGON /RL HIGHEST /RU \"{user}\" /IT");
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                key.SetValue(Name, Command);
            }

            return;
        }

        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            key?.DeleteValue(Name, throwOnMissingValue: false);
        }

        if (HasScheduledTask(Name) && Schtasks($"/Delete /F /TN \"{Name}\"") != 0)
        {
            throw new InvalidOperationException("Run MicControl as administrator to remove the elevated startup task.");
        }
    }

    /// <summary>Replaces autostart entries left by MicSwitch with MicControlNG ones (keeping autostart on if it was).</summary>
    public void MigrateLegacyEntries()
    {
        var hadRunKey = HasRunKey(LegacyName);
        var hadTask = HasScheduledTask(LegacyName);
        if (!hadRunKey && !hadTask)
        {
            return;
        }

        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            key?.DeleteValue(LegacyName, throwOnMissingValue: false);
        }

        if (hadTask)
        {
            // Needs elevation; if it fails the old task just points at a missing executable.
            Schtasks($"/Delete /F /TN \"{LegacyName}\"");
        }

        if (!IsEnabled)
        {
            SetEnabled(true);
        }
    }

    private static bool HasRunKey(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(name) is not null;
    }

    private static bool HasScheduledTask(string name) => Schtasks($"/Query /TN \"{name}\"") == 0;

    private static int Schtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
