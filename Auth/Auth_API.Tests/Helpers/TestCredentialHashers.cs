using System.Security.Cryptography;
using Auth.Application.Security.Keyring;
using Auth.Infrastructure.Security;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The real credential hasher over a keyring a test controls, for tests that are
/// about the HMAC itself (a ticket signature, an OTP digest), not a stand-in.
/// </summary>
internal static class TestCredentialHashers
{
    /// <summary>A never-rotated ring: one key, key 1, issuing the legacy form.</summary>
    public static KeyringCredentialHasher SingleKey(byte[]? material = null) =>
        Over(HmacKeyring.FromSingleKey(material ?? RandomNumberGenerator.GetBytes(32)));

    /// <summary>The hasher over <paramref name="ring"/>, at <paramref name="clock"/>'s time.</summary>
    public static KeyringCredentialHasher Over(HmacKeyring ring, TimeProvider? clock = null) =>
        new(new HmacKeyringHolder(ring), clock ?? TimeProvider.System);
}
