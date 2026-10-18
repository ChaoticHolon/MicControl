using System.Security.Cryptography;
using System.Text;
using MicControl.Installation;
using MicControl.Updates;

namespace MicControl.Tests;

public sealed class ReleaseFeedTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Release(string tag = "v2.1.0", string asset = "MicControl-Setup-2.1.0.exe", string? digest = Digest,
        string url = "https://github.com/ChaoticHolon/MicControl/releases/download/v2.1.0/MicControl-Setup-2.1.0.exe",
        bool draft = false, bool prerelease = false, string? sumsUrl = null) => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt", "digest": "{{Digest}}" },
            {{(sumsUrl is null ? "" : $"{{ \"name\": \"SHA512SUMS\", \"browser_download_url\": \"{sumsUrl}\" }},")}}
            { "name": "{{asset}}", "browser_download_url": "{{url}}"{{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }
          ]
        }
        """;

    private const string SumsUrl = "https://github.com/ChaoticHolon/MicControl/releases/download/v2.1.0/SHA512SUMS";

    private static readonly byte[] Setup = Encoding.UTF8.GetBytes("installer bytes");

    private static string Sha512Hex => Convert.ToHexStringLower(SHA512.HashData(Setup));

    [Fact]
    public void Parse_FindsTheSetupAndItsDigest()
    {
        var release = ReleaseFeed.Parse(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 1, 0), release.Version);
        Assert.Equal("MicControl-Setup-2.1.0.exe", release.FileName);
        Assert.Equal("https", release.SetupUrl.Scheme);
        Assert.Equal(Digest["sha256:".Length..], release.Sha256);
        Assert.Null(release.Sha512SumsUrl);
    }

    [Fact]
    public void Parse_FindsSha512Sums_AndAcceptsItWithoutADigest()
    {
        var release = ReleaseFeed.Parse(Release(digest: null, sumsUrl: SumsUrl));

        Assert.NotNull(release);
        Assert.Null(release.Sha256);
        Assert.Equal(new Uri(SumsUrl), release.Sha512SumsUrl);
        // Only over HTTPS; without it and without a digest there's nothing to verify against.
        Assert.Null(ReleaseFeed.Parse(Release(digest: null, sumsUrl: "http://example.com/SHA512SUMS")));
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

    [Fact]
    public void ChooseChecksum_PrefersSha512Sums_AndFallsBackToGitHubsSha256()
    {
        var both = ReleaseFeed.Parse(Release(sumsUrl: SumsUrl))!;
        var listed = $"{Sha512Hex}  MicControl-Setup-2.1.0.exe\n";

        Assert.Equal(new ExpectedChecksum(HashAlgorithmName.SHA512, Sha512Hex), ReleaseFeed.ChooseChecksum(both, listed));
        // SHA512SUMS missing, unreadable or not listing the setup: GitHub's digest.
        var sha256 = new ExpectedChecksum(HashAlgorithmName.SHA256, Digest["sha256:".Length..]);
        Assert.Equal(sha256, ReleaseFeed.ChooseChecksum(both, null));
        Assert.Equal(sha256, ReleaseFeed.ChooseChecksum(both, "nothing here"));
        // Neither: nothing to verify against.
        Assert.Null(ReleaseFeed.ChooseChecksum(ReleaseFeed.Parse(Release(digest: null, sumsUrl: SumsUrl))!, "nothing here"));
    }

    [Theory]
    [InlineData("v2.1.0", "MicControl-Setup-2.0.0.exe")] // installer for another version
    [InlineData("nightly", "MicControl-Setup-2.1.0.exe")] // tag isn't a version
    public void Parse_WithoutAMatchingSetup_ReturnsNull(string tag, string asset)
    {
        Assert.Null(ReleaseFeed.Parse(Release(tag, asset)));
    }

    [Fact]
    public void Parse_RefusesWhatCantBeVerifiedOrIsntFinal()
    {
        Assert.Null(ReleaseFeed.Parse(Release(digest: null)));
        Assert.Null(ReleaseFeed.Parse(Release(digest: "md5:abc")));
        Assert.Null(ReleaseFeed.Parse(Release(url: "http://example.com/MicControl-Setup-2.1.0.exe")));
        Assert.Null(ReleaseFeed.Parse(Release(draft: true)));
        Assert.Null(ReleaseFeed.Parse(Release(prerelease: true)));
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
    public async Task Matches_ChecksTheContent_WithEitherAlgorithm()
    {
        var token = TestContext.Current.CancellationToken;
        var sha512 = new ExpectedChecksum(HashAlgorithmName.SHA512, Sha512Hex);
        var sha256 = new ExpectedChecksum(HashAlgorithmName.SHA256, Convert.ToHexStringLower(SHA256.HashData(Setup)));

        Assert.True(await ReleaseFeed.MatchesAsync(new MemoryStream(Setup), sha512, token));
        Assert.True(await ReleaseFeed.MatchesAsync(new MemoryStream(Setup), sha256, token));
        Assert.True(await ReleaseFeed.MatchesAsync(new MemoryStream(Setup), sha512 with { Hex = Sha512Hex.ToUpperInvariant() }, token));
        Assert.False(await ReleaseFeed.MatchesAsync(new MemoryStream([1, 2, 3]), sha512, token));
        Assert.False(await ReleaseFeed.MatchesAsync(new MemoryStream([1, 2, 3]), sha256, token));
    }
}
