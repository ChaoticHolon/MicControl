using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using MicControl.Platform;
using MicControl.Updates;
using Microsoft.Extensions.Logging;

namespace MicControl;

/// <summary>
/// Updates from GitHub Releases: offers a newer release only when its <c>SHA512SUMS</c> carries the developer's
/// signature (<see cref="ReleaseKeys"/>), downloads the setup (which is the app itself), checks it against that signed
/// SHA-512, and runs it silently in the same scope (all users or just this user) as the current install. Development
/// builds aren't installed and only report that.
/// </summary>
public sealed partial class UpdateService(IInstaller installer, ILogger<UpdateService> logger) : IUpdateService, IDisposable
{
    private const string DownloadFolderPrefix = "MicControl-update-";
    private readonly HttpClient http = CreateClient();
    private InstallerRelease? pending;
    private string? pendingSha512;

    public bool IsInstalled => installer.Current is not null;

    public async Task<string?> CheckAsync()
    {
        if (installer.Current is not { } current)
        {
            return null;
        }

        string feed;
        try
        {
            feed = await http.GetStringAsync(ReleaseFeed.LatestReleaseUrl).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // GitHub's answer while nothing is published yet (drafts don't count): nothing to update to.
            LogNoReleases();
            pending = null;
            pendingSha512 = null;
            return null;
        }

        var release = ReleaseFeed.Parse(feed);
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        LogChecked(current.Scope, version, release?.Version);
        pending = null;
        pendingSha512 = null;
        if (release is null || !ReleaseFeed.IsNewer(release.Version, version))
        {
            return null;
        }

        // Verified now, so a release that can't be installed is never offered.
        var sums = await http.GetByteArrayAsync(release.Sha512SumsUrl).ConfigureAwait(false);
        var signature = await http.GetStringAsync(release.SignatureUrl).ConfigureAwait(false);
        if (ReleaseFeed.VerifiedSha512(release, sums, signature, ReleaseKeys.Trusted) is not { } sha512)
        {
            LogNotSigned(release.Version, ReleaseKeys.Trusted.Count);
            return null;
        }

        LogSignatureVerified(release.Version);
        pending = release;
        pendingSha512 = sha512;
        return release.Version.ToString(3);
    }

    public async Task<bool> InstallAsync()
    {
        if (pending is not { } release || pendingSha512 is not { } sha512 || installer.Current is not { } current)
        {
            return false;
        }

        // A new folder each time, so nothing can be put in place there beforehand.
        RemoveOldDownloads();
        var folder = Directory.CreateTempSubdirectory(DownloadFolderPrefix).FullName;
        var file = Path.Combine(folder, release.FileName);
        LogDownloading(release.SetupUrl, file);
        try
        {
            using var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var output = File.Create(file);
            await using (output.ConfigureAwait(false))
            {
                await response.Content.CopyToAsync(output).ConfigureAwait(false);
            }
        }
        catch
        {
            // A partial download is never left behind.
            Directory.Delete(folder, recursive: true);
            throw;
        }

        // Held open (others may only read) from the check until the setup has started, so the file that runs is the
        // file that was verified.
        var check = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (check.ConfigureAwait(false))
        {
            if (!await ReleaseFeed.MatchesAsync(check, sha512).ConfigureAwait(false))
            {
                LogChecksumMismatch(file);
                check.Close();
                Directory.Delete(folder, recursive: true);
                throw new InvalidOperationException("The downloaded update didn't match its signed checksum, so it wasn't run.");
            }

            LogVerified(file);
            var arguments = ReleaseFeed.SilentInstallArguments(current.Scope);
            LogStartingInstaller(file, arguments);
            // Shell execute so an all-users update can ask for admin rights if this process doesn't have them.
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true })?.Dispose();
        }

        return true;
    }

    /// <summary>Earlier updates' downloads (the setup they ran has finished by now); one still in use stays.</summary>
    private void RemoveOldDownloads()
    {
        try
        {
            foreach (var old in Directory.EnumerateDirectories(Path.GetTempPath(), DownloadFolderPrefix + "*"))
            {
                try
                {
                    Directory.Delete(old, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogOldDownloadKept(old, ex);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOldDownloadKept(DownloadFolderPrefix + "*", ex);
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Release {Version} isn't signed by a trusted release key ({Keys} trusted), or its SHA512SUMS doesn't list the setup; not offering it")]
    private partial void LogNotSigned(Version version, int keys);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check: no release is published yet")]
    private partial void LogNoReleases();

    [LoggerMessage(Level = LogLevel.Information, Message = "Release {Version}: SHA512SUMS signature verified")]
    private partial void LogSignatureVerified(Version version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloaded update {File} doesn't match its signed SHA-512; deleted it")]
    private partial void LogChecksumMismatch(string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded update {File} matches its signed SHA-512")]
    private partial void LogVerified(string file);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kept the earlier update download {Folder}")]
    private partial void LogOldDownloadKept(string folder, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting setup {File} {Arguments}")]
    private partial void LogStartingInstaller(string file, string arguments);
}
