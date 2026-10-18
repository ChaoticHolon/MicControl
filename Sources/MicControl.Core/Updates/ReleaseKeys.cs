namespace MicControl.Updates;

/// <summary>
/// Public keys whose signature of a release's <c>SHA512SUMS</c> the updater accepts, as <c>ssh-keygen</c> prints
/// them (<c>tools\Signing.ps1 NewReleaseKey</c>). The private keys never leave the developer's PC. To change keys,
/// ship a version that trusts both the old and the new key, sign the following releases with the new one, and drop
/// the old key a release later.
/// </summary>
public static class ReleaseKeys
{
    public static IReadOnlyList<string> Trusted { get; } =
    [
    ];
}
