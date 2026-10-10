using System.Security.Cryptography;
using System.Text;
using MicControlNG.Installation;
using MicControlNG.Updates;

namespace MicControlNG.Tests;

public sealed class ReleaseFeedTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Release(string tag = "v2.1.0", string asset = "MicControlNG-Setup-2.1.0.exe", string? digest = Digest,
        string url = "https://github.com/ChaoticHolon/MicControlNG/releases/download/v2.1.0/MicControlNG-Setup-2.1.0.exe",
        bool draft = false, bool prerelease = false) => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt", "digest": "{{Digest}}" },
            { "name": "{{asset}}", "browser_download_url": "{{url}}"{{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }
          ]
        }
        """;

    [Fact]
    public void Parse_FindsTheSetupAndItsDigest()
    {
        var release = ReleaseFeed.Parse(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 1, 0), release.Version);
        Assert.Equal("MicControlNG-Setup-2.1.0.exe", release.FileName);
        Assert.Equal("https", release.SetupUrl.Scheme);
        Assert.Equal(Digest["sha256:".Length..], release.Sha256);
    }

    [Theory]
    [InlineData("v2.1.0", "MicControlNG-Setup-2.0.0.exe")] // installer for another version
    [InlineData("nightly", "MicControlNG-Setup-2.1.0.exe")] // tag isn't a version
    public void Parse_WithoutAMatchingSetup_ReturnsNull(string tag, string asset)
    {
        Assert.Null(ReleaseFeed.Parse(Release(tag, asset)));
    }

    [Fact]
    public void Parse_RefusesWhatCantBeVerifiedOrIsntFinal()
    {
        Assert.Null(ReleaseFeed.Parse(Release(digest: null)));
        Assert.Null(ReleaseFeed.Parse(Release(digest: "md5:abc")));
        Assert.Null(ReleaseFeed.Parse(Release(url: "http://example.com/MicControlNG-Setup-2.1.0.exe")));
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
        var command = SetupCommand.Parse(ReleaseFeed.SilentInstallArguments(scope).Split(' '), "MicControlNG-Setup-2.1.0.exe");

        Assert.Equal(new SetupCommand(SetupMode.Install, Silent: true, Scope: scope, Relaunch: true), command);
    }

    [Fact]
    public async Task MatchesSha256_ChecksTheContent()
    {
        var content = Encoding.UTF8.GetBytes("installer bytes");
        var hex = Convert.ToHexStringLower(SHA256.HashData(content));

        Assert.True(await ReleaseFeed.MatchesSha256Async(new MemoryStream(content), hex, TestContext.Current.CancellationToken));
        Assert.True(await ReleaseFeed.MatchesSha256Async(new MemoryStream(content), hex.ToUpperInvariant(), TestContext.Current.CancellationToken));
        Assert.False(await ReleaseFeed.MatchesSha256Async(new MemoryStream([1, 2, 3]), hex, TestContext.Current.CancellationToken));
    }
}
