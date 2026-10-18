using System.Security.Cryptography;
using System.Text;
using MicControl.Installation;
using MicControl.Updates;

namespace MicControl.Tests;

public sealed class ReleaseFeedTests
{
    private const string Base = "https://github.com/ChaoticHolon/MicControl/releases/download/v2.1.0/";

    private static string Release(string tag = "v2.1.0", string asset = "MicControl-Setup-2.1.0.exe", string url = Base + "MicControl-Setup-2.1.0.exe",
        bool draft = false, bool prerelease = false, string? sumsUrl = Base + "SHA512SUMS", string? signatureUrl = Base + "SHA512SUMS.sig") => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt" },
            {{(sumsUrl is null ? "" : $"{{ \"name\": \"SHA512SUMS\", \"browser_download_url\": \"{sumsUrl}\" }},")}}
            {{(signatureUrl is null ? "" : $"{{ \"name\": \"SHA512SUMS.sig\", \"browser_download_url\": \"{signatureUrl}\" }},")}}
            { "name": "{{asset}}", "browser_download_url": "{{url}}", "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" }
          ]
        }
        """;

    // SecurityTests.Sums lists the SHA-512 of these bytes for MicControl-Setup-2.1.0.exe.
    private static readonly byte[] Setup = Encoding.UTF8.GetBytes("installer bytes");

    private static string Sha512Hex => Convert.ToHexStringLower(SHA512.HashData(Setup));

    [Fact]
    public void Parse_FindsTheSetup_ItsChecksums_AndTheirSignature()
    {
        var release = ReleaseFeed.Parse(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 1, 0), release.Version);
        Assert.Equal("MicControl-Setup-2.1.0.exe", release.FileName);
        Assert.Equal(new Uri(Base + "MicControl-Setup-2.1.0.exe"), release.SetupUrl);
        Assert.Equal(new Uri(Base + "SHA512SUMS"), release.Sha512SumsUrl);
        Assert.Equal(new Uri(Base + "SHA512SUMS.sig"), release.SignatureUrl);
    }

    [Fact]
    public void Parse_RefusesAReleaseThatCantBeVerifiedOrIsntFinal()
    {
        // GitHub's own digest no longer counts: anyone who can edit the release can change it.
        Assert.Null(ReleaseFeed.Parse(Release(signatureUrl: null)));
        Assert.Null(ReleaseFeed.Parse(Release(sumsUrl: null)));
        Assert.Null(ReleaseFeed.Parse(Release(signatureUrl: "http://example.com/SHA512SUMS.sig")));
        Assert.Null(ReleaseFeed.Parse(Release(sumsUrl: "http://example.com/SHA512SUMS")));
        Assert.Null(ReleaseFeed.Parse(Release(url: "http://example.com/MicControl-Setup-2.1.0.exe")));
        Assert.Null(ReleaseFeed.Parse(Release(draft: true)));
        Assert.Null(ReleaseFeed.Parse(Release(prerelease: true)));
    }

    [Fact]
    public void VerifiedSha512_OnlyFromSumsSignedByATrustedKey()
    {
        var release = ReleaseFeed.Parse(Release())!;
        var sums = Encoding.UTF8.GetBytes(SecurityTests.Sums);
        string[] trusted = [SecurityTests.ReleaseKey];

        Assert.Equal(Sha512Hex, ReleaseFeed.VerifiedSha512(release, sums, SecurityTests.GoodSignature, trusted));
        Assert.Null(ReleaseFeed.VerifiedSha512(release, sums, SecurityTests.OtherKeySignature, trusted));
        Assert.Null(ReleaseFeed.VerifiedSha512(release, sums, SecurityTests.GitNamespaceSignature, trusted));
        Assert.Null(ReleaseFeed.VerifiedSha512(release, sums, SecurityTests.GoodSignature, []));
        Assert.Null(ReleaseFeed.VerifiedSha512(release, [.. sums, (byte)'\n'], SecurityTests.GoodSignature, trusted));
        // Signed, but for another file name: nothing to check the setup against.
        var other = ReleaseFeed.Parse(Release("v2.2.0", "MicControl-Setup-2.2.0.exe"))!;
        Assert.Null(ReleaseFeed.VerifiedSha512(other, sums, SecurityTests.GoodSignature, trusted));
    }

    [Fact]
    public void ReleaseKeys_AreEd25519PublicKeys_ListedInDocsReleaseSigners()
    {
        Assert.All(ReleaseKeys.Trusted, k => Assert.NotNull(MicControl.Security.SshSignature.ParsePublicKey(k)));

        // tools/Signing.ps1 and anyone checking a download by hand use docs/release_signers; the app uses ReleaseKeys.
        var signers = File.ReadAllLines(Path.Combine(Ui.ScreenshotTests.RepositoryRoot(), "docs", "release_signers"))
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .ToList();
        Assert.All(signers, l => Assert.StartsWith($"release@miccontrol namespaces=\"{ReleaseFeed.SignatureNamespace}\" ssh-ed25519 ", l, StringComparison.Ordinal));
        Assert.Equal(
            ReleaseKeys.Trusted.Select(k => k.Split(' ')[1]).Order(StringComparer.Ordinal),
            signers.Select(l => l.Split(' ')[3]).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("{0}  MicControl-Setup-2.1.0.exe\n")]
    [InlineData("{0} *MicControl-Setup-2.1.0.exe\r\n")] // binary mode marker, Windows line ending
    [InlineData("{1}  Other.exe\n{0}  micontrolng-typo.exe\n{0}  MicControl-Setup-2.1.0.exe")]
    public void FindSha512_ReadsSha512sumFormat(string format)
    {
        var sums = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Sha512Hex.ToUpperInvariant(), new string('a', 128));

        Assert.Equal(Sha512Hex, ReleaseFeed.FindSha512(sums, "MicControl-Setup-2.1.0.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc  MicControl-Setup-2.1.0.exe")] // not a SHA-512
    [InlineData("{0}  MicControl-Setup-2.0.0.exe")] // another file
    public void FindSha512_WithoutAnEntry_ReturnsNull(string format)
    {
        var sums = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Sha512Hex);

        Assert.Null(ReleaseFeed.FindSha512(sums, "MicControl-Setup-2.1.0.exe"));
    }

    [Theory]
    [InlineData("v2.1.0", "MicControl-Setup-2.0.0.exe")] // installer for another version
    [InlineData("nightly", "MicControl-Setup-2.1.0.exe")] // tag isn't a version
    public void Parse_WithoutAMatchingSetup_ReturnsNull(string tag, string asset)
    {
        Assert.Null(ReleaseFeed.Parse(Release(tag, asset)));
    }

    [Theory]
    [InlineData("2.1.0", "2.0.0.0", true)]
    [InlineData("2.0.0", "2.0.0.0", false)] // the assembly version has a fourth part
    [InlineData("v2.0.1", "2.0.0", true)]
    [InlineData("2.0.0", "2.1.0", false)]
    [InlineData("10.0.0", "9.9.9", true)]
    public void IsNewer_ComparesVersions(string latest, string current, bool expected)
    {
        Assert.True(ReleaseFeed.TryParseVersion(latest, out var l));
        Assert.Equal(expected, ReleaseFeed.IsNewer(l, Version.Parse(current)));
    }

    [Theory]
    [InlineData(InstallScope.AllUsers)]
    [InlineData(InstallScope.CurrentUser)]
    public void SilentInstallArguments_RunTheSetupSilently_InTheSameScope_AndRelaunch(InstallScope scope)
    {
        var command = SetupCommand.Parse(ReleaseFeed.SilentInstallArguments(scope).Split(' '), "MicControl-Setup-2.1.0.exe");

        Assert.Equal(new SetupCommand(SetupMode.Install, Silent: true, Scope: scope, Relaunch: true), command);
    }

    [Fact]
    public async Task Matches_ChecksTheContent()
    {
        var token = TestContext.Current.CancellationToken;

        Assert.True(await ReleaseFeed.MatchesAsync(new MemoryStream(Setup), Sha512Hex, token));
        Assert.True(await ReleaseFeed.MatchesAsync(new MemoryStream(Setup), Sha512Hex.ToUpperInvariant(), token));
        Assert.False(await ReleaseFeed.MatchesAsync(new MemoryStream([1, 2, 3]), Sha512Hex, token));
    }
}
