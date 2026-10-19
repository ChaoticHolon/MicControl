using System.Security.Cryptography;
using System.Text;
using MicControl.Security;

namespace MicControl.Tests;

public sealed class SecurityTests
{
    // RFC 8032 section 7.1 tests 1 and 2, and a 300-byte message signed by OpenSSL (Python cryptography).
    [Theory]
    [InlineData("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a", "",
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b")]
    [InlineData("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c", "72",
        "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00")]
    [InlineData("6bfca1b24d8f9baed321c6176c60602d32b8a5e0cff8d750235dbdca04d8509f",
        "3ff4d058f6450e07f4bdcb9f5f643e0b2465fc5bcf434e648ac955e9b289842dc6882a14abf1440867e16cf3a502f6817d970d6f351581dc24920f8b93f22195414b5cbcf9ef35668cd0ce866f9f4f9833f3a795792741c90dc0ad7b2ef8633927acca5b54e619f40a96fe87e68172e036dbecf135b6e07cd880d66681c0a896989d88476b2b8f4f6737dbca8a577b71e270c7d49836ce44a44eccc97f837c741039a358c4dd0900b3416c0178c92b0b39d5effee7c888cf1eddfead8e415e12ce237d535b2f628fc791cdee24faa098265c328e19a97191186a9d2079e3c32431dc65c03124e85e2232de64ef03514c88b5e82f54fadc7f3f4a25b6d22a379c59569f892c8cdf5be33b97fe596ed07396ea910de8805b9553bf717114aee35a1d7dc43715411d6c922efe71",
        "0f5d2c935c6273f49c8182ffe310c0c31e40e28a0fbd4c648f3f2b9ebfb393c8a61405e101f75f9f7fdf81b486c2cf714ba23bfd4f9816234e721c693fca8207")]
    public void Ed25519_AcceptsValidSignatures_AndRejectsAnyChange(string publicKey, string message, string signature)
    {
        var key = Convert.FromHexString(publicKey);
        var data = Convert.FromHexString(message);
        var sig = Convert.FromHexString(signature);
        Assert.True(Ed25519.Verify(key, data, sig));

        Assert.False(Ed25519.Verify(key, [.. data, 0], sig));
        for (var i = 0; i < sig.Length; i += 7)
        {
            var changed = sig.ToArray();
            changed[i] ^= 0x01;
            Assert.False(Ed25519.Verify(key, data, changed), $"signature byte {i} changed");
        }

        var otherKey = key.ToArray();
        otherKey[0] ^= 0x01;
        Assert.False(Ed25519.Verify(otherKey, data, sig));
        Assert.False(Ed25519.Verify(key.AsSpan(1), data, sig));
        Assert.False(Ed25519.Verify(key, data, sig.AsSpan(1)));
    }

    [Fact]
    public void Ed25519_RejectsANonCanonicalS()
    {
        // S + L verifies the same equation, but RFC 8032 requires S < L, so a signature can't be re-encoded.
        var key = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var sig = Convert.FromHexString("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
        var l = System.Numerics.BigInteger.Pow(2, 252) + System.Numerics.BigInteger.Parse("27742317777372353535851937790883648493", System.Globalization.CultureInfo.InvariantCulture);
        var s = new System.Numerics.BigInteger(sig.AsSpan(32), isUnsigned: true) + l;
        var malleable = sig.ToArray();
        s.TryWriteBytes(malleable.AsSpan(32), out _, isUnsigned: true);

        Assert.False(Ed25519.Verify(key, [], malleable));
    }

    // Made with OpenSSH 9.6: ssh-keygen -Y sign -f release -n miccontrol-release SHA512SUMS
    internal const string ReleaseKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGLTkZz6/cpdn0E8BdTcbLyra/ddxpzX8XQEgPVxlUkw test release key";

    internal const string Sums = "6860154acde0508ec43840aacba3b8ee2ae7301e0b06c29d4e4b3196ffaf7c15919c19cc169d0b416d9d55a2e1b078721a024e4c7de12127b77dcc64d034afff  MicControl-Setup-2.1.0.exe\n";

    internal const string GoodSignature = """
        -----BEGIN SSH SIGNATURE-----
        U1NIU0lHAAAAAQAAADMAAAALc3NoLWVkMjU1MTkAAAAgYtORnPr9yl2fQTwF1NxsvKtr91
        3GnNfxdASA9XGVSTAAAAASbWljY29udHJvbC1yZWxlYXNlAAAAAAAAAAZzaGE1MTIAAABT
        AAAAC3NzaC1lZDI1NTE5AAAAQG/NPDayo3FW41iLPVuL6lDj5+qgR2BsvpRQyHC1awQjke
        gHbP4mWaiM6cycDG2gyqkt1gCrAZ0qSV5IPcTaUA8=
        -----END SSH SIGNATURE-----
        """;

    // Same file, signed by a key that isn't trusted.
    internal const string OtherKeySignature = """
        -----BEGIN SSH SIGNATURE-----
        U1NIU0lHAAAAAQAAADMAAAALc3NoLWVkMjU1MTkAAAAgcoEbGAHsc9t+GkOMGo0s/4p1Qe
        9qFlGAu7edU7F/94sAAAASbWljY29udHJvbC1yZWxlYXNlAAAAAAAAAAZzaGE1MTIAAABT
        AAAAC3NzaC1lZDI1NTE5AAAAQBMRxp8ykZQuzlOHcoKeTgwcVYD1FRLVClOmeIISw1r6/8
        kxA4bZ6nUBsXdLwVHUrdWGLx50GZgt1s5i4PfTuwA=
        -----END SSH SIGNATURE-----
        """;

    // Same file and key, signed for the "git" namespace.
    internal const string GitNamespaceSignature = """
        -----BEGIN SSH SIGNATURE-----
        U1NIU0lHAAAAAQAAADMAAAALc3NoLWVkMjU1MTkAAAAgYtORnPr9yl2fQTwF1NxsvKtr91
        3GnNfxdASA9XGVSTAAAAADZ2l0AAAAAAAAAAZzaGE1MTIAAABTAAAAC3NzaC1lZDI1NTE5
        AAAAQFPR+ppjLo3yFol24R7flqMSjuy6zo7LWlS38dE5P87asKjxqROmzSNBIeoeEjcAKK
        FoWL2KiFw5EmMPE4jKOAI=
        -----END SSH SIGNATURE-----
        """;

    [Fact]
    public void SshSignature_AcceptsOpenSshsSignature_FromATrustedKey()
    {
        Assert.True(SshSignature.Verify(Encoding.UTF8.GetBytes(Sums), GoodSignature, "miccontrol-release", [ReleaseKey]));
        Assert.True(SshSignature.Verify(Encoding.UTF8.GetBytes(Sums), GoodSignature.Replace("\n", "\r\n", StringComparison.Ordinal), "miccontrol-release", ["ssh-ed25519 junk", ReleaseKey]));
    }

    [Fact]
    public void SshSignature_RefusesEverythingElse()
    {
        var sums = Encoding.UTF8.GetBytes(Sums);
        Assert.False(SshSignature.Verify(sums, OtherKeySignature, "miccontrol-release", [ReleaseKey]));
        Assert.False(SshSignature.Verify(sums, GitNamespaceSignature, "miccontrol-release", [ReleaseKey]));
        Assert.False(SshSignature.Verify(sums, GoodSignature, "git", [ReleaseKey]));
        Assert.False(SshSignature.Verify(sums, GoodSignature, "miccontrol-release", []));
        Assert.False(SshSignature.Verify(Encoding.UTF8.GetBytes(Sums.Replace("6860", "6861", StringComparison.Ordinal)), GoodSignature, "miccontrol-release", [ReleaseKey]));
        Assert.False(SshSignature.Verify(sums, "", "miccontrol-release", [ReleaseKey]));
        Assert.False(SshSignature.Verify(sums, GoodSignature.Replace("U1NIU0lH", "U1NIU0lI", StringComparison.Ordinal), "miccontrol-release", [ReleaseKey]));
        Assert.False(SshSignature.Verify(sums, GoodSignature.Replace("-----END", "!!\n-----END", StringComparison.Ordinal), "miccontrol-release", [ReleaseKey]));
    }

    [Fact]
    public void SshSignature_ParsesOnlyEd25519PublicKeys()
    {
        Assert.NotNull(SshSignature.ParsePublicKey(ReleaseKey));
        Assert.Null(SshSignature.ParsePublicKey("ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC7"));
        Assert.Null(SshSignature.ParsePublicKey("ssh-ed25519 not-base64!"));
        Assert.Null(SshSignature.ParsePublicKey(""));
    }

    public static TheoryData<SecretBoxAlgorithm> SupportedAlgorithms()
    {
        var data = new TheoryData<SecretBoxAlgorithm>();
        foreach (var algorithm in Enum.GetValues<SecretBoxAlgorithm>().Where(SecretBox.IsSupported))
        {
            data.Add(algorithm);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SupportedAlgorithms))]
    public void SecretBox_RoundTrips_AndDetectsTampering(SecretBoxAlgorithm algorithm)
    {
        var key = SecretBox.NewKey();
        var plaintext = Encoding.UTF8.GetBytes("""{ "token": "secret" }""");
        var sealedData = SecretBox.Seal(key, plaintext, "settings.json"u8, algorithm);

        Assert.Equal(plaintext.Length + SecretBox.Overhead, sealedData.Length);
        Assert.Equal((byte)algorithm, sealedData[0]);
        Assert.Equal(plaintext, SecretBox.Open(key, sealedData, "settings.json"u8));
        Assert.NotEqual(sealedData, SecretBox.Seal(key, plaintext, "settings.json"u8, algorithm)); // a fresh nonce each time

        Assert.ThrowsAny<CryptographicException>(() => SecretBox.Open(SecretBox.NewKey(), sealedData, "settings.json"u8));
        Assert.ThrowsAny<CryptographicException>(() => SecretBox.Open(key, sealedData, "other.json"u8));
        foreach (var index in new[] { 5, SecretBox.NonceSize + 3, sealedData.Length - 1 })
        {
            var changed = sealedData.ToArray();
            changed[index] ^= 0x80;
            Assert.ThrowsAny<CryptographicException>(() => SecretBox.Open(key, changed, "settings.json"u8));
        }
    }

    [Fact]
    public void SecretBox_RefusesBadInput()
    {
        var key = SecretBox.NewKey();
        Assert.Throws<ArgumentException>(() => SecretBox.Seal(key.AsSpan(1), "x"u8));
        Assert.Throws<CryptographicException>(() => SecretBox.Open(key, new byte[SecretBox.Overhead - 1]));
        Assert.Throws<CryptographicException>(() => SecretBox.Open(key, new byte[SecretBox.Overhead + 4]));
        Assert.Equal([], SecretBox.Open(key, SecretBox.Seal(key, [])));
    }
}
