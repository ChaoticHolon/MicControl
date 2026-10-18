using System.Security.Cryptography;
using System.Text.Json;
using MicControl.Installation;

namespace MicControl.Updates;

/// <summary>A published installer: its version, where to download it, and what it can be verified against.</summary>
/// <param name="Sha256">The SHA-256 digest GitHub reports for the setup, if any: the fallback check.</param>
/// <param name="Sha512SumsUrl">The release's <c>SHA512SUMS</c> file, if attached: the preferred check.</param>
public sealed record InstallerRelease(Version Version, Uri SetupUrl, string FileName, string? Sha256, Uri? Sha512SumsUrl);

/// <summary>The hash a download must match, as lowercase hex.</summary>
public sealed record ExpectedChecksum(HashAlgorithmName Algorithm, string Hex);

/// <summary>
/// Reads MicControl releases from GitHub and decides what to install. The installer is
/// <c>MicControl-Setup-&lt;version&gt;.exe</c>, the app itself (see <see cref="SetupCommand"/>), attached to each release.
/// </summary>
public static class ReleaseFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/ChaoticHolon/MicControl/releases/latest";

    public const string SetupFilePrefix = SetupCommand.SetupFilePrefix + "-";

    /// <summary>Checksums file attached to each release, in <c>sha512sum</c> format.</summary>
    public const string Sha512SumsFileName = "SHA512SUMS";

    /// <summary>
    /// The installer in GitHub's "latest release" JSON, or <c>null</c> when there is nothing safe to install:
    /// a draft or prerelease, a tag that isn't a version, no matching setup file over HTTPS, or nothing to verify it
    /// against (neither a <c>SHA512SUMS</c> file nor a SHA-256 digest from GitHub).
    /// </summary>
    public static InstallerRelease? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (IsTrue(root, "draft") || IsTrue(root, "prerelease")
            || !root.TryGetProperty("tag_name", out var tag) || !TryParseVersion(tag.GetString(), out var version)
            || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var expectedName = $"{SetupFilePrefix}{version.ToString(3)}.exe";
        Uri? setupUrl = null;
        Uri? sumsUrl = null;
        string? sha256 = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                setupUrl = HttpsUrl(asset);
                var digest = Text(asset, "digest");
                sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest["sha256:".Length..].ToLowerInvariant()
                    : null;
            }
            else if (string.Equals(name, Sha512SumsFileName, StringComparison.Ordinal))
            {
                sumsUrl = HttpsUrl(asset);
            }
        }

        return setupUrl is null || (sha256 is null && sumsUrl is null)
            ? null
            : new InstallerRelease(version, setupUrl, expectedName, sha256, sumsUrl);
    }

    /// <summary>
    /// The SHA-512 that a <c>SHA512SUMS</c> file (<c>sha512sum</c> format: "hash  name" or "hash *name" per line) lists
    /// for <paramref name="fileName"/>, or <c>null</c> if it isn't listed.
    /// </summary>
    public static string? FindSha512(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var trimmed = line.Trim();
            var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
            if (space != 128 || !trimmed[..space].All(Uri.IsHexDigit))
            {
                continue;
            }

            var name = trimmed[(space + 1)..].TrimStart().TrimStart('*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..space].ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// What the setup is verified against: its SHA-512 from the release's <c>SHA512SUMS</c> when that lists it,
    /// otherwise GitHub's SHA-256 digest; <c>null</c> when neither is available.
    /// </summary>
    /// <param name="sha512Sums">The downloaded <c>SHA512SUMS</c>, or <c>null</c> if there is none or it couldn't be read.</param>
    public static ExpectedChecksum? ChooseChecksum(InstallerRelease release, string? sha512Sums) =>
        sha512Sums is not null && FindSha512(sha512Sums, release.FileName) is { } sha512 ? new ExpectedChecksum(HashAlgorithmName.SHA512, sha512)
        : release.Sha256 is { } sha256 ? new ExpectedChecksum(HashAlgorithmName.SHA256, sha256)
        : null;

    /// <summary>Accepts "2.1.0" or "v2.1.0"; the result always has major, minor and build.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        var trimmed = text?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(trimmed, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    /// <summary>Compares ignoring missing parts, so 2.0.0 equals the assembly version 2.0.0.0.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    /// <summary>
    /// Arguments for a silent update in the same scope as the current install; the new setup starts the app again
    /// when it's done.
    /// </summary>
    public static string SilentInstallArguments(InstallScope scope) =>
        new SetupCommand(SetupMode.Install, Silent: true, Scope: scope, Relaunch: true).ToArguments();

    public static async Task<bool> MatchesAsync(Stream content, ExpectedChecksum expected, CancellationToken cancellationToken = default)
    {
        var hash = expected.Algorithm == HashAlgorithmName.SHA512
            ? await SHA512.HashDataAsync(content, cancellationToken).ConfigureAwait(false)
            : expected.Algorithm == HashAlgorithmName.SHA256
                ? await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false)
                : throw new ArgumentException($"Unsupported checksum algorithm {expected.Algorithm}", nameof(expected));
        return string.Equals(Convert.ToHexStringLower(hash), expected.Hex, StringComparison.OrdinalIgnoreCase);
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static Uri? HttpsUrl(JsonElement asset) =>
        Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
