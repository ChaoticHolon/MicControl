using MicControlNG.Platform;
using Velopack;
using Velopack.Sources;

namespace MicControlNG;

/// <summary>Self-update through Velopack from GitHub Releases. Portable/dev builds only report availability.</summary>
public sealed class UpdateService : IUpdateService
{
    private readonly UpdateManager manager = new(new GithubSource(ViewModels.MainViewModel.RepositoryUrl, null, false));
    private UpdateInfo? pending;

    public bool IsInstalled => manager.IsInstalled;

    /// <returns>The newer version, or <c>null</c> when up to date.</returns>
    public async Task<string?> CheckAsync()
    {
        if (!manager.IsInstalled)
        {
            return null;
        }

        pending = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return pending?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAndRestartAsync()
    {
        if (pending is null)
        {
            return;
        }

        await manager.DownloadUpdatesAsync(pending).ConfigureAwait(false);
        manager.ApplyUpdatesAndRestart(pending.TargetFullRelease);
    }
}
