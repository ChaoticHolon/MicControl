using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using MicControl.Platform;
using MicControl.Updates;
using Microsoft.Extensions.Logging;

namespace MicControl;

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

        // Prefer the release's SHA512SUMS; fall back to the SHA-256 digest GitHub reports for the setup.
        string? sums = null;
        if (release.Sha512SumsUrl is { } sumsUrl)
        {
            try
            {
                sums = await http.GetStringAsync(sumsUrl).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                LogSumsUnavailable(sumsUrl, ex);
            }
        }

        if (ReleaseFeed.ChooseChecksum(release, sums) is not { } checksum)
        {
            LogNoChecksum(release.FileName, sums is not null);
            throw new InvalidOperationException("The update has no checksum to verify it against, so it wasn't downloaded.");
        }

        if (sums is not null && checksum.Algorithm != HashAlgorithmName.SHA512)
        {
            LogNotInSums(release.FileName);
        }

        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MicControl-update")).FullName;
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
            if (!await ReleaseFeed.MatchesAsync(check, checksum).ConfigureAwait(false))
            {
                LogChecksumMismatch(file, checksum.Algorithm.Name);
                check.Close();
                File.Delete(file);
                throw new InvalidOperationException("The downloaded update didn't match its published checksum, so it wasn't run.");
            }
        }

        LogVerified(file, checksum.Algorithm.Name);
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MicControl/{Assembly.GetEntryAssembly()?.GetName().Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check ({Scope} install): running {Current}, latest release {Latest}")]
    private partial void LogChecked(Installation.InstallScope scope, Version current, Version? latest);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {Url} to {File}")]
    private partial void LogDownloading(Uri url, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read {Url}; falling back to GitHub's SHA-256 digest for the update, if it has one")]
    private partial void LogSumsUnavailable(Uri url, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SHA512SUMS doesn't list {File}; verifying it with GitHub's SHA-256 digest instead")]
    private partial void LogNotInSums(string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update {File} has nothing to verify it against (SHA512SUMS read: {SumsRead}, no SHA-256 digest); not installing it")]
    private partial void LogNoChecksum(string file, bool sumsRead);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloaded update {File} failed its {Algorithm} check; deleted it")]
    private partial void LogChecksumMismatch(string file, string? algorithm);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded update {File} matches its {Algorithm} checksum")]
    private partial void LogVerified(string file, string? algorithm);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting setup {File} {Arguments}")]
    private partial void LogStartingInstaller(string file, string arguments);
}
