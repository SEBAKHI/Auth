using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Infrastructure.Authentication;
using Auth_API.Common.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S12: the validation profiles hold no key. Each takes the keys of the token in front of it from
/// the signer's key set at that moment: the key its <c>kid</c> names, or every live key when the set
/// holds no such <c>kid</c>, and never a key outside the set. RS256 stays the only algorithm in
/// every profile. Tokens are real, validated by the handler JwtBearer runs.
/// </summary>
public sealed class AccessTokenKeySetTests : IDisposable
{
    private const string Issuer = "https://auth.example.com";
    private const string PlatformAudience = "authsystem-api";
    private const string Application = "shop";

    private static readonly JwtSettings Settings = new()
    {
        Issuer = Issuer,
        Audience = PlatformAudience,
        KeyId = "key-a"
    };

    private readonly RSA _a = RSA.Create(2048);
    private readonly RSA _b = RSA.Create(2048);
    private readonly RSA _stranger = RSA.Create(2048);
    private readonly KeySet _set;

    public AccessTokenKeySetTests()
    {
        _set = new KeySet(Key(_a, "key-a"), Key(_b, "key-b"));
    }

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
        _stranger.Dispose();
    }

    public enum Profile { Platform, UserInfo, Machine }

    // --- the algorithm ---

    [Theory]
    [InlineData(Profile.Platform)]
    [InlineData(Profile.UserInfo)]
    [InlineData(Profile.Machine)]
    public async Task EveryProfile_RefusesRs512_UnderAKeyOfTheSet(Profile profile)
    {
        var parameters = Parameters(profile, _set);

        (await PassesAsync(TokenFor(profile, Key(_a, "key-a"), SecurityAlgorithms.RsaSha256), parameters))
            .Should().BeTrue("the same token under RS256 passes, so only the algorithm can refuse it");
        (await PassesAsync(TokenFor(profile, Key(_a, "key-a"), SecurityAlgorithms.RsaSha512), parameters))
            .Should().BeFalse("RS256 is the only algorithm this server signs with");
    }

    // --- which keys a token is tried against ---

    [Theory]
    [InlineData(Profile.Platform)]
    [InlineData(Profile.UserInfo)]
    [InlineData(Profile.Machine)]
    public async Task ATokenWhoseKidIsInTheSet_IsVerifiedByThatKey(Profile profile)
    {
        (await PassesAsync(TokenFor(profile, Key(_b, "key-b")), Parameters(profile, _set))).Should().BeTrue();
    }

    [Theory]
    [InlineData("unknown-kid")]
    [InlineData(null)]
    public async Task ATokenWhoseKidTheSetDoesNotHold_IsTriedAgainstEveryLiveKey(string? kid)
    {
        // Signed by the second key of the set, so trying only the first would refuse it.
        var token = TokenFor(Profile.Platform, Key(_b, kid));

        (await PassesAsync(token, Parameters(Profile.Platform, _set))).Should().BeTrue();
    }

    [Fact]
    public async Task AKidOfTheSet_NamesItsKey_SoAnotherKeysSignatureUnderIt_IsRefused()
    {
        var token = TokenFor(Profile.Platform, Key(_b, "key-a"));

        (await PassesAsync(token, Parameters(Profile.Platform, _set))).Should().BeFalse(
            "the kid of a live key is tried against that key alone");
    }

    [Theory]
    [InlineData("key-a")]
    [InlineData("unknown-kid")]
    [InlineData(null)]
    public async Task AKeyOutsideTheSet_VerifiesNothing(string? kid)
    {
        var token = TokenFor(Profile.Platform, Key(_stranger, kid));

        (await PassesAsync(token, Parameters(Profile.Platform, _set))).Should().BeFalse();
    }

    [Fact]
    public async Task TheParameters_ReadTheSetPerToken_NotOnceWhenBuilt()
    {
        // Built once, as each bearer scheme builds them at startup.
        var set = new KeySet(Key(_a, "key-a"));
        var parameters = Parameters(Profile.Platform, set);
        var byA = TokenFor(Profile.Platform, Key(_a, "key-a"));
        var byB = TokenFor(Profile.Platform, Key(_b, "key-b"));

        (await PassesAsync(byA, parameters)).Should().BeTrue();
        (await PassesAsync(byB, parameters)).Should().BeFalse();

        set.Keys = [Key(_b, "key-b")];

        (await PassesAsync(byA, parameters)).Should().BeFalse("a key the set dropped verifies nothing from then on");
        (await PassesAsync(byB, parameters)).Should().BeTrue("a key the set added is trusted without a new build");
    }

    [Fact]
    public async Task TheSignersOwnSet_VerifiesWhatItSigns_AndTheSignersKidIsTheConfiguredOne()
    {
        using var rsa = RSA.Create(2048);
        var set = SigningKeyRing.Single(rsa, "auth-key-1", new SigningKeySource(SigningKeyLoader.PemKey, PlainText: false));
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(Descriptor(Profile.Platform, set.Current));

        handler.ReadJsonWebToken(token).Kid.Should().Be("auth-key-1");
        set.PublishedKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().Equal("auth-key-1");
        (await PassesAsync(token, Parameters(Profile.Platform, set))).Should().BeTrue();
    }

    // --- the signing keyring (S12 PR B) ---

    [Fact]
    public async Task APreviousKey_IsRefusedOnceItsWindowEnds_WithNoRebuild()
    {
        // TS2: the parameters are built once, at start; the ring decides per token what is live.
        var previous = Auth_API.Tests.SecretManagement.KeyringTestHarness.NewSigningKey();
        var keys = Holder(previous with { VerifyUntilUtc = DateTime.UtcNow.AddSeconds(2) }, staged: null);
        var parameters = Parameters(Profile.Platform, keys);
        var byPrevious = TokenFor(Profile.Platform, Signer(previous));

        (await PassesAsync(byPrevious, parameters)).Should().BeTrue("within its window a previous key is accepted");

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        (await PassesAsync(byPrevious, parameters)).Should().BeFalse("past its window it is refused, with no restart and no new parameters");
    }

    [Fact]
    public async Task ACompromisedKey_IsRefusedAtOnce()
    {
        var staged = Auth_API.Tests.SecretManagement.KeyringTestHarness.NewSigningKey();
        var keys = Holder(previous: null, staged);
        var parameters = Parameters(Profile.Platform, keys);
        var leaked = TokenFor(Profile.Platform, keys.Current.Key);
        (await PassesAsync(leaked, parameters)).Should().BeTrue();

        keys.TryApply(ring => ring.PromoteEmergency(DateTime.UtcNow)).Should().BeTrue("this process loaded the staged key");

        (await PassesAsync(leaked, parameters)).Should().BeFalse("an emergency closes the leaked key in this process at once");
        (await PassesAsync(TokenFor(Profile.Platform, keys.Current.Key), parameters)).Should().BeTrue();
    }

    [Fact]
    public async Task TheStagedKey_IsAcceptedBeforeItSigns()
    {
        // A token signed by the staged key is accepted from the moment it is loaded, before any
        // process signs with it: another worker that promoted first is not refused here.
        var staged = Auth_API.Tests.SecretManagement.KeyringTestHarness.NewSigningKey();
        var keys = Holder(previous: null, staged);
        var byStaged = TokenFor(Profile.Platform, Signer(staged));

        keys.Current.Key.KeyId.Should().NotBe(staged.Kid);
        (await PassesAsync(byStaged, Parameters(Profile.Platform, keys))).Should().BeTrue();
    }

    #region Helpers

    // A loaded signing ring: a current key, and a previous or a staged one.
    private static SigningKeyRingHolder Holder(Auth.Application.Security.Keyring.SigningKey? previous, Auth.Application.Security.Keyring.SigningKey? staged)
    {
        var current = Auth_API.Tests.SecretManagement.KeyringTestHarness.NewSigningKey() with { CreatedAtUtc = DateTime.UtcNow.AddDays(-1) };
        var keys = new List<Auth.Application.Security.Keyring.SigningKey> { current };
        if (previous is not null)
        {
            keys.Add(previous with { CreatedAtUtc = DateTime.UtcNow.AddDays(-2), PrivateKeyPem = null });
        }

        if (staged is not null)
        {
            keys.Add(staged with { CreatedAtUtc = DateTime.UtcNow, PublishedAtUtc = DateTime.UtcNow });
        }

        var ring = Auth.Application.Security.Keyring.RsaKeyring.Create(keys, current.Kid, staged?.Kid, removed: null).Value;
        return new SigningKeyRingHolder(SigningKeyRing.Load(ring, new SigningKeySource(SigningKeyLoader.PemKey, PlainText: false)));
    }

    // The private half of a ring key, under its kid: what another process signing with it holds.
    private static RsaSecurityKey Signer(Auth.Application.Security.Keyring.SigningKey key)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKeyPem);
        return new RsaSecurityKey(rsa) { KeyId = key.Kid };
    }

    private static RsaSecurityKey Key(RSA rsa, string? kid) => new(rsa) { KeyId = kid };

    private static TokenValidationParameters Parameters(Profile profile, ISigningKeyMaterial set) => profile switch
    {
        Profile.Platform => AccessTokenValidation.Platform(Settings, set),
        Profile.UserInfo => AccessTokenValidation.UserInfo(Settings, set),
        _ => AccessTokenValidation.Machine(Settings, set)
    };

    private static string TokenFor(Profile profile, SecurityKey key, string algorithm = SecurityAlgorithms.RsaSha256) =>
        new JsonWebTokenHandler().CreateToken(Descriptor(profile, new SigningCredentials(key, algorithm)));

    /// <summary>A token each profile accepts, but for the key and the algorithm it is signed with.</summary>
    private static SecurityTokenDescriptor Descriptor(Profile profile, SigningCredentials credentials)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object> { ["jti"] = Guid.NewGuid().ToString() }
        };

        switch (profile)
        {
            case Profile.Platform:
                descriptor.Audience = PlatformAudience;
                descriptor.Claims["sub"] = Guid.NewGuid().ToString();
                break;
            case Profile.UserInfo:
                descriptor.Audience = Application;
                descriptor.Claims["sub"] = Guid.NewGuid().ToString();
                break;
            default:
                descriptor.TokenType = "at+jwt";
                descriptor.Audience = MachineClients.Audience;
                descriptor.Claims["sub"] = Application;
                descriptor.Claims["client_id"] = Application;
                break;
        }

        return descriptor;
    }

    private static async Task<bool> PassesAsync(string token, TokenValidationParameters parameters) =>
        (await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, parameters)).IsValid;

    /// <summary>A key set whose keys a test can change, as a rotation does; the first one signs.</summary>
    private sealed class KeySet(params RsaSecurityKey[] keys) : ISigningKeyMaterial
    {
        public RsaSecurityKey[] Keys { get; set; } = keys;

        public SigningCredentials Current => new(Keys[0], SecurityAlgorithms.RsaSha256);

        public IReadOnlyList<SecurityKey> VerificationKeys(DateTimeOffset now) => Keys;

        public IReadOnlyList<RsaSecurityKey> PublishedKeys(DateTimeOffset now) => Keys;
    }

    #endregion
}
