using System.Security.Cryptography;
using System.Text.Json;
using MicControlNG.Installation;

namespace MicControlNG.Updates;

/// <summary>A published installer: its version, where to download it, and the SHA-256 that GitHub reports for it.</summary>
public sealed record InstallerRelease(Version Version, Uri SetupUrl, string FileName, string Sha256);

/// <summary>
/// Reads MicControl releases from GitHub and decides what to install. The installer is
/// <c>MicControlNG-Setup-&lt;version&gt;.exe</c>, the app itself (see <see cref="SetupCommand"/>), attached to each release.
/// </summary>
public static class ReleaseFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/ChaoticHolon/MicControlNG/releases/latest";

    public const string SetupFilePrefix = SetupCommand.SetupFilePrefix + "-";

    /// <summary>
    /// The installer in GitHub's "latest release" JSON, or <c>null</c> when there is nothing safe to install:
    /// a draft or prerelease, a tag that isn't a version, no matching setup file, or no SHA-256 digest to verify it.
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
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(Text(asset, "name"), expectedName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var digest = Text(asset, "digest");
            var url = Text(asset, "browser_download_url");
            if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            return new InstallerRelease(version, uri, expectedName, digest["sha256:".Length..].ToLowerInvariant());
        }

        return null;
    }

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

    public static async Task<bool> MatchesSha256Async(Stream content, string expectedHex, CancellationToken cancellationToken = default)
    {
        var hash = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexStringLower(hash), expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
