using MicControl.Platform;

namespace MicControl.Windows;

public static class WindowsPlatform
{
    /// <summary>Names the single-instance mutex and the show/exit signals. They're per sign-in session ("Local\").</summary>
    public const string InstanceId = "MicControl-{567EBFFF-E391-4B38-AC85-469978EB37C4}";

    public const string MutexName = $@"Local\{InstanceId}";

    /// <summary>Set by a second launch: the running copy shows its window.</summary>
    public const string ShowSignalName = $@"Local\{InstanceId}-show";

    /// <summary>Set by the installer and uninstaller: the running copy exits cleanly so its files can be replaced.</summary>
    public const string ExitSignalName = $@"Local\{InstanceId}-exit";

    /// <summary>Keeps two setups from running at once.</summary>
    public const string SetupMutexName = $@"Local\{InstanceId}-setup";

    public static PlatformCapabilities Capabilities { get; } = new()
    {
        CanSuppressHotkeys = true,
        SupportsMouseHotkeys = true,
        LimitationNotice = StartupService.IsElevated
            ? null
            : "MicControl isn't running as administrator. Hotkeys won't work while an elevated app or game is focused.",
    };
}
