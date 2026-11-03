using System.Security.Cryptography;
using Auth.Application.Configuration;
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
        var set = new SingleSigningKeyMaterial(rsa, "auth-key-1");
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(Descriptor(Profile.Platform, set.Current));

        handler.ReadJsonWebToken(token).Kid.Should().Be("auth-key-1");
        set.PublishedKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().Equal("auth-key-1");
        (await PassesAsync(token, Parameters(Profile.Platform, set))).Should().BeTrue();
    }

    #region Helpers

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
