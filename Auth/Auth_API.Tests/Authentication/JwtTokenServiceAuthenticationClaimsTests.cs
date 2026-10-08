using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S08 T3: amr, auth_time and mfa_req as a relying party reads them — from the raw
/// payload of a real token, not from the claims object, because one claim with one
/// value serializes as a plain string where OIDC Core §2 requires amr to be a JSON
/// array.
/// </summary>
public sealed class JwtTokenServiceAuthenticationClaimsTests : IDisposable
{
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 8, 9, 30, 15, TimeSpan.Zero);

    private readonly User _user = TestHelpers.CreateUser(email: "admin@example.com");

    private readonly JwtTokenService _service = new(
        Options.Create(new JwtSettings
        {
            Issuer = "https://auth.example.com",
            Audience = "auth-platform",
            KeyId = "test-key"
        }),
        Mock.Of<IPasswordHasher>());

    public void Dispose() => _service.Dispose();

    private static JsonElement DecodePayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment)));
        return document.RootElement.Clone();
    }

    private JsonElement Mint(AccessTokenAuthentication authentication, string? audience = null) =>
        DecodePayload(_service.GenerateAccessToken(
            _user, ["users:read"], ["admin"], authentication, Guid.NewGuid(), audience: audience));

    [Fact]
    public void Amr_IsAJsonArray_EvenWithOneValue()
    {
        var payload = Mint(new AccessTokenAuthentication(AuthenticationMethods.Password, StartedAt, MfaRequirement.None));

        var amr = payload.GetProperty("amr");
        amr.ValueKind.Should().Be(JsonValueKind.Array, "OIDC Core §2: amr is a JSON array of strings");
        amr.EnumerateArray().Select(value => value.GetString()).Should().Equal("pwd");
    }

    [Fact]
    public void AuthTime_IsANumber_TheSessionStart()
    {
        var payload = Mint(new AccessTokenAuthentication(
            AuthenticationMethods.Password.With(AuthenticationMethods.Totp), StartedAt, MfaRequirement.None));

        var authTime = payload.GetProperty("auth_time");
        authTime.ValueKind.Should().Be(JsonValueKind.Number);
        authTime.GetInt64().Should().Be(StartedAt.ToUnixTimeSeconds());
        payload.GetProperty("amr").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("pwd", "otp", "mfa");
    }

    [Fact]
    public void Unknown_EmitsNeitherAmrNorAuthTime()
    {
        var payload = Mint(new AccessTokenAuthentication(AuthenticationMethods.Unknown, StartedAt, MfaRequirement.None));

        payload.TryGetProperty("amr", out _).Should().BeFalse();
        payload.TryGetProperty("auth_time", out _).Should().BeFalse("the two are emitted together or not at all");
    }

    [Fact]
    public void KnownMethods_WithoutATime_EmitNeither()
    {
        var payload = Mint(new AccessTokenAuthentication(AuthenticationMethods.Password, null, MfaRequirement.None));

        payload.TryGetProperty("amr", out _).Should().BeFalse();
        payload.TryGetProperty("auth_time", out _).Should().BeFalse();
    }

    [Fact]
    public void ExternalIdentityAlone_HasAnAuthTime_ButNoEmptyAmr()
    {
        // No registered amr value describes an external identity (RFC 8176): an
        // empty array would say nothing, so it is left out, and auth_time stays.
        var payload = Mint(new AccessTokenAuthentication(AuthenticationMethods.ExternalIdentity, StartedAt, MfaRequirement.None));

        payload.TryGetProperty("amr", out _).Should().BeFalse();
        payload.GetProperty("auth_time").GetInt64().Should().Be(StartedAt.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData(MfaRequirement.Enroll, "enroll")]
    [InlineData(MfaRequirement.StepUp, "step_up")]
    [InlineData(MfaRequirement.Reauthenticate, "reauthenticate")]
    public void MfaReq_IsWrittenOnlyWhenSomethingIsRequired(MfaRequirement requirement, string value)
    {
        var payload = Mint(new AccessTokenAuthentication(AuthenticationMethods.Password, StartedAt, requirement));

        payload.GetProperty("mfa_req").ValueKind.Should().Be(JsonValueKind.String);
        payload.GetProperty("mfa_req").GetString().Should().Be(value);
    }

    [Fact]
    public void MfaReq_None_IsAbsent()
    {
        var payload = Mint(new AccessTokenAuthentication(AuthenticationMethods.Password, StartedAt, MfaRequirement.None));

        payload.TryGetProperty("mfa_req", out _).Should().BeFalse();
    }

    /// <summary>
    /// The AppTokenAuthenticationClaimsTests rule (ARR-0744): an access token issued
    /// to an application carries no amr, no acr, no auth_time and no mfa_req.
    /// </summary>
    [Fact]
    public void AppToken_Unrecorded_CarriesNoAuthenticationClaims()
    {
        var payload = Mint(AccessTokenAuthentication.Unrecorded, audience: "EDIS");

        payload.TryGetProperty("amr", out _).Should().BeFalse();
        payload.TryGetProperty("acr", out _).Should().BeFalse();
        payload.TryGetProperty("auth_time", out _).Should().BeFalse();
        payload.TryGetProperty("mfa_req", out _).Should().BeFalse();
    }

    [Fact]
    public void TheOtherClaims_AreUnchanged()
    {
        // amr and auth_time are added through the payload dictionary; nothing the
        // subject already carried moves or changes shape.
        var withAuthentication = Mint(new AccessTokenAuthentication(AuthenticationMethods.Password, StartedAt, MfaRequirement.None));
        var without = Mint(AccessTokenAuthentication.Unrecorded);

        withAuthentication.EnumerateObject().Select(p => p.Name).Except(["amr", "auth_time"])
            .Should().BeEquivalentTo(without.EnumerateObject().Select(p => p.Name));
        withAuthentication.GetProperty("permissions").GetString().Should().Be("users:read");
        withAuthentication.GetProperty("roles").GetString().Should().Be("admin");
        withAuthentication.GetProperty("sub").GetString().Should().Be(_user.Id.ToString());
    }
}
