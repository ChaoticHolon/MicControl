using System.Security.Cryptography;
using System.Text.Json;
using MicControl.Installation;
using MicControl.Security;

namespace MicControl.Updates;

/// <summary>A published installer: its version, where to download it, and the signed checksums that verify it.</summary>
/// <param name="Sha512SumsUrl">The release's <c>SHA512SUMS</c>.</param>
/// <param name="SignatureUrl">The developer's signature of it, <c>SHA512SUMS.sig</c>.</param>
public sealed record InstallerRelease(Version Version, Uri SetupUrl, string FileName, Uri Sha512SumsUrl, Uri SignatureUrl);

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
    /// <c>ssh-keygen -Y sign -n miccontrol-release</c>'s signature of <see cref="Sha512SumsFileName"/>, made with the
    /// developer's release key (<see cref="ReleaseKeys"/>) on their own PC.
    /// </summary>
    public const string SignatureFileName = Sha512SumsFileName + ".sig";

    /// <summary>What release signatures are made for (<c>ssh-keygen -n</c>), so no other signature of that key passes.</summary>
    public const string SignatureNamespace = "miccontrol-release";

    /// <summary>
    /// The installer in GitHub's "latest release" JSON, or <c>null</c> when there is nothing safe to install:
    /// a draft or prerelease, a tag that isn't a version, or no matching setup, <c>SHA512SUMS</c> and signature over
    /// HTTPS.
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
        Uri? signatureUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                setupUrl = HttpsUrl(asset);
            }
            else if (string.Equals(name, Sha512SumsFileName, StringComparison.Ordinal))
            {
                sumsUrl = HttpsUrl(asset);
            }
            else if (string.Equals(name, SignatureFileName, StringComparison.Ordinal))
            {
                signatureUrl = HttpsUrl(asset);
            }
        }

        return setupUrl is null || sumsUrl is null || signatureUrl is null
            ? null
            : new InstallerRelease(version, setupUrl, expectedName, sumsUrl, signatureUrl);
    }

    /// <summary>
    /// The SHA-512 that a <c>SHA512SUMS</c> file (<c>sha512sum</c> format: "hash  name" or "hash *name" per line) lists
    /// for <paramref name="fileName"/>, or <c>null</c> if it isn't listed.
    /// </summary>
    public static string? FindSha512(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            // A byte order mark (from an editor or Windows PowerShell 5.1's UTF-8) isn't part of the hash.
            var trimmed = line.Trim().TrimStart('\uFEFF');
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
    /// The SHA-512 the setup must have: its entry in <paramref name="sha512Sums"/>, but only when
    /// <paramref name="signature"/> is a valid signature of exactly those bytes by one of
    /// <paramref name="trustedKeys"/>. <c>null</c> otherwise: an unsigned release is never installed.
    /// </summary>
    public static string? VerifiedSha512(InstallerRelease release, ReadOnlySpan<byte> sha512Sums, string signature, IEnumerable<string> trustedKeys)
    {
        ArgumentNullException.ThrowIfNull(release);
        return SshSignature.Verify(sha512Sums, signature, SignatureNamespace, trustedKeys)
            ? FindSha512(System.Text.Encoding.UTF8.GetString(sha512Sums), release.FileName)
            : null;
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

    public static async Task<bool> MatchesAsync(Stream content, string sha512, CancellationToken cancellationToken = default)
    {
        var hash = await SHA512.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexStringLower(hash), sha512, StringComparison.OrdinalIgnoreCase);
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static Uri? HttpsUrl(JsonElement asset) =>
        Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
