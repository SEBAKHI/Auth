using Auth.Application.Features.Authentication.RevokeToken;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Common.Authentication;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// The revocation endpoint's test of "a token this server signed" (OI-65), resolved from the SAME
/// registration Program.cs calls, so what is tested is what runs. It must accept what either
/// bearer scheme accepts, refuse what both refuse, and never throw.
/// </summary>
public sealed class IssuedAccessTokenValidatorTests : IDisposable
{
    private readonly UserInfoTokens _tokens = new();
    private readonly User _user = TestHelpers.CreateUser();
    private readonly ServiceProvider _provider;
    private readonly IIssuedAccessTokenValidator _validator;

    public IssuedAccessTokenValidatorTests()
    {
        var services = new ServiceCollection();
        services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
        _provider = services.BuildServiceProvider();
        _validator = _provider.GetRequiredService<IIssuedAccessTokenValidator>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _tokens.Dispose();
    }

    [Fact]
    public void TheRegistration_RegistersTheValidatorOverTheSchemeProfiles()
    {
        _validator.Should().BeOfType<IssuedAccessTokenValidator>();
    }

    [Fact]
    public async Task ValidateAsync_ApplicationToken_ReturnsItsClaimsUnderTheirOwnNames()
    {
        var sessionId = Guid.NewGuid();
        var token = _tokens.ForApplication(_user, "openid", sessionId: sessionId);

        var result = await _validator.ValidateAsync(token);

        result.IsError.Should().BeFalse();
        result.Value.FindFirst("jti")?.Value.Should().Be(_tokens.Service.GetTokenId(token));
        result.Value.FindFirst("sid")?.Value.Should().Be(sessionId.ToString());
        long.TryParse(result.Value.FindFirst("exp")?.Value, out _).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_PlatformToken_ReturnsItsClaims()
    {
        var token = _tokens.ForPlatform(_user);

        var result = await _validator.ValidateAsync(token);

        result.IsError.Should().BeFalse();
        result.Value.FindFirst("jti")?.Value.Should().Be(_tokens.Service.GetTokenId(token));
    }

    [Fact]
    public async Task ValidateAsync_EmptyToken_ReturnsInvalidToken()
    {
        var result = await _validator.ValidateAsync(string.Empty);

        result.FirstError.Code.Should().Be(AuthErrors.InvalidToken.Code);
    }

    [Fact]
    public void RefusedCases_IsNotEmpty() =>
        RefusedAccessTokens.Cases.Count<object[]>().Should().BeGreaterThan(10);

    [Theory]
    [MemberData(nameof(RefusedAccessTokens.Cases), MemberType = typeof(RefusedAccessTokens))]
    public async Task ValidateAsync_TokenBothSchemesRefuse_ReturnsInvalidToken_WithoutThrowing(string refused)
    {
        var token = RefusedAccessTokens.Build(refused, _tokens, _user.Id);

        var result = await _validator.ValidateAsync(token);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidToken.Code);
    }

    [Theory]
    [MemberData(nameof(RefusedAccessTokens.Cases), MemberType = typeof(RefusedAccessTokens))]
    public async Task Revoke_TokenBothSchemesRefuse_Answers200_AndStoresNothing(string refused)
    {
        // The endpoint is anonymous: a token that is not provably ours must leave no trace.
        var blacklist = new Mock<ITokenBlacklistService>();
        var revocation = new Mock<ICredentialRevocationService>();
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        var handler = new RevokeTokenCommandHandler(
            _validator,
            blacklist.Object,
            refreshTokens.Object,
            new Mock<IRefreshTokenKeyService>().Object,
            revocation.Object,
            new Mock<ILogger<RevokeTokenCommandHandler>>().Object);
        var token = RefusedAccessTokens.Build(refused, _tokens, _user.Id);

        var result = await handler.Handle(
            new RevokeTokenCommand(token, TokenTypeHint.AccessToken, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        blacklist.VerifyNoOtherCalls();
        revocation.VerifyNoOtherCalls();
        refreshTokens.VerifyNoOtherCalls();
    }
}
