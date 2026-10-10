using System.Diagnostics;
using System.Reflection;
using MicControlNG.Platform;
using MicControlNG.Updates;
using Microsoft.Extensions.Logging;

namespace MicControlNG;

/// <summary>
/// Updates from GitHub Releases: downloads the newer setup (which is the app itself), verifies it, and runs it silently
/// in the same scope (all users or just this user) as the current install. Development builds aren't installed and
/// only report that.
/// </summary>
public sealed partial class UpdateService(IInstaller installer, ILogger<UpdateService> logger) : IUpdateService, IDisposable
{
    private readonly HttpClient http = CreateClient();
    private InstallerRelease? pending;

    public bool IsInstalled => installer.Current is not null;

    public async Task<string?> CheckAsync()
    {
        if (installer.Current is not { } current)
        {
            return null;
        }

        var release = ReleaseFeed.Parse(await http.GetStringAsync(ReleaseFeed.LatestReleaseUrl).ConfigureAwait(false));
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        pending = release is not null && ReleaseFeed.IsNewer(release.Version, version) ? release : null;
        LogChecked(current.Scope, version, release?.Version);
        return pending?.Version.ToString(3);
    }

    public async Task<bool> InstallAsync()
    {
        if (pending is not { } release || installer.Current is not { } current)
        {
            return false;
        }

        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MicControlNG-update")).FullName;
        var file = Path.Combine(folder, release.FileName);
        LogDownloading(release.SetupUrl, file);
        using (var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var output = File.Create(file);
            await using (output.ConfigureAwait(false))
            {
                await response.Content.CopyToAsync(output).ConfigureAwait(false);
            }
        }

        var check = File.OpenRead(file);
        await using (check.ConfigureAwait(false))
        {
            if (!await ReleaseFeed.MatchesSha256Async(check, release.Sha256).ConfigureAwait(false))
            {
                LogChecksumMismatch(file);
                check.Close();
                File.Delete(file);
                throw new InvalidOperationException("The downloaded update didn't match its published checksum, so it wasn't run.");
            }
        }

        var arguments = ReleaseFeed.SilentInstallArguments(current.Scope);
        LogStartingInstaller(file, arguments);
        // Shell execute so an all-users update can ask for admin rights if this process doesn't have them.
        Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true })?.Dispose();
        return true;
    }

    public void Dispose() => http.Dispose();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MicControlNG/{Assembly.GetEntryAssembly()?.GetName().Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check ({Scope} install): running {Current}, latest release {Latest}")]
    private partial void LogChecked(Installation.InstallScope scope, Version current, Version? latest);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {Url} to {File}")]
    private partial void LogDownloading(Uri url, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloaded update {File} failed its SHA-256 check; deleted it")]
    private partial void LogChecksumMismatch(string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting setup {File} {Arguments}")]
    private partial void LogStartingInstaller(string file, string arguments);
}
