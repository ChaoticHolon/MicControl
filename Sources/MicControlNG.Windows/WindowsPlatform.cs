using MicControlNG.Platform;

namespace MicControlNG.Windows;

public static class WindowsPlatform
{
    public static PlatformCapabilities Capabilities { get; } = new()
    {
        CanSuppressHotkeys = true,
        SupportsMouseHotkeys = true,
        LimitationNotice = StartupService.IsElevated
            ? null
            : "MicControl isn't running as administrator. Hotkeys won't work while an elevated app or game is focused.",
    };
}
