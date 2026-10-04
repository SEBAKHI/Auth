using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Covers the profile claims a relying party reads from the access token:
/// phone_number, phone_number_verified and picture. A real
/// <see cref="JwtTokenService"/> signs with a throwaway in-memory key, and every
/// assertion reads the decoded payload JSON — the form a relying party sees —
/// because a <c>Claim</c> object cannot show whether "false" left as a JSON
/// boolean or as a string.
/// </summary>
public sealed class JwtTokenServiceProfileClaimsTests : IDisposable
{
    private const string PhoneNumber = "+971 (50) 123-4567";
    private const string PictureUrl = "https://auth.example.com/uploads/images/avatars/user.png";

    private readonly JwtTokenService _service = new(
        Options.Create(new JwtSettings
        {
            Issuer = "https://auth.example.com",
            Audience = "auth-platform",
            KeyId = "test-key"
        }),
        Mock.Of<IPasswordHasher>());

    public void Dispose() => _service.Dispose();

    private static User CreateUser(string? phoneNumber = PhoneNumber, bool phoneConfirmed = false)
    {
        var user = User.Create(
            email: "user@example.com",
            passwordHash: "hash",
            firstName: "Test",
            lastName: "User",
            createdBy: Guid.Empty,
            phoneNumber: phoneNumber);

        if (phoneConfirmed)
        {
            user.ConfirmPhone(user.Id);
        }

        return user;
    }

    private JsonElement MintPayload(User user, string? pictureUrl = null, string? audience = null)
    {
        var token = _service.GenerateAccessToken(
            user, [], [], sessionId: null, organizationPermissions: null, audience, pictureUrl);
        return DecodePayload(token);
    }

    private static JsonElement DecodePayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment)));
        return document.RootElement.Clone();
    }

    [Fact]
    public void GenerateAccessToken_UnverifiedPhone_WritesNumberAndFalseBoolean()
    {
        var payload = MintPayload(CreateUser());

        payload.GetProperty("phone_number").GetString().Should().Be(PhoneNumber);
        // A boolean, not the string "false": a relying party testing the
        // value for truth would read the string "false" as true.
        payload.GetProperty("phone_number_verified").ValueKind.Should().Be(JsonValueKind.False);
    }

    [Fact]
    public void GenerateAccessToken_DigitsOnlyPhone_StaysAJsonStringWithItsLeadingZero()
    {
        // A number-shaped value must not become a JSON number: that would drop
        // the leading zero and change what a relying party reads.
        var payload = MintPayload(CreateUser(phoneNumber: "0501234567"));

        payload.GetProperty("phone_number").ValueKind.Should().Be(JsonValueKind.String);
        payload.GetProperty("phone_number").GetString().Should().Be("0501234567");
    }

    [Fact]
    public void GenerateAccessToken_VerifiedPhone_WritesTrueBoolean()
    {
        var payload = MintPayload(CreateUser(phoneConfirmed: true));

        payload.GetProperty("phone_number_verified").ValueKind.Should().Be(JsonValueKind.True);
    }

    [Fact]
    public void GenerateAccessToken_NoPhone_WritesNeitherPhoneClaim()
    {
        var payload = MintPayload(CreateUser(phoneNumber: null));

        payload.TryGetProperty("phone_number", out _).Should().BeFalse();
        payload.TryGetProperty("phone_number_verified", out _).Should().BeFalse();
    }

    [Fact]
    public void GenerateAccessToken_AbsoluteHttpsPicture_WritesPicture()
    {
        var payload = MintPayload(CreateUser(), PictureUrl);

        payload.GetProperty("picture").GetString().Should().Be(PictureUrl);
    }

    /// <summary>
    /// Values that must not become a "picture" claim: the relative path the
    /// default image base produces, nothing at all, and schemes a relying
    /// party must never be handed as an image address.
    /// </summary>
    public static TheoryData<string?> UnusablePictureValues => new()
    {
        "/uploads/images/a.png",
        null,
        "",
        "ftp://h/a.png",
        "javascript:alert(1)",
    };

    [Fact]
    public void UnusablePictureValues_AreNotEmpty()
    {
        UnusablePictureValues.Count<object[]>().Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(UnusablePictureValues))]
    public void GenerateAccessToken_UnusablePicture_WritesNoPicture(string? pictureUrl)
    {
        var payload = MintPayload(CreateUser(), pictureUrl);

        payload.TryGetProperty("picture", out _).Should().BeFalse();
    }

    [Fact]
    public void GenerateAccessToken_ApplicationAudience_CarriesProfileClaimsToo()
    {
        var payload = MintPayload(CreateUser(), PictureUrl, audience: "edis");

        payload.GetProperty("aud").GetString().Should().Be("edis");
        payload.GetProperty("phone_number").GetString().Should().Be(PhoneNumber);
        payload.GetProperty("phone_number_verified").ValueKind.Should().Be(JsonValueKind.False);
        payload.GetProperty("picture").GetString().Should().Be(PictureUrl);
    }

    [Fact]
    public void GenerateAccessToken_WithoutPhoneOrPicture_KeepsThePreviousClaimSetAndValidates()
    {
        var token = _service.GenerateAccessToken(CreateUser(phoneNumber: null), ["users:read"], ["Admin"]);
        var payload = DecodePayload(token);

        // The claim set a token carried before these claims existed — nothing
        // added, nothing lost, and still signed for the platform audience.
        payload.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "sub", "email", "jti", "iat", "name", "given_name", "family_name",
            "locale", "timezone", "theme", "roles", "permissions",
            "nbf", "exp", "iss", "aud");
        payload.GetProperty("aud").GetString().Should().Be("auth-platform");
        payload.GetProperty("iss").GetString().Should().Be("https://auth.example.com");
        (payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64())
            .Should().Be(15 * 60);
        _service.ValidateAccessToken(token).IsError.Should().BeFalse();
    }
}
