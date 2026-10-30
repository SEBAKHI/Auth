using System.Security.Claims;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Common.Authentication;

/// <summary>
/// <see cref="IApplicationAccessTokenValidator"/> over the userinfo scheme's own parameters
/// (<see cref="AccessTokenValidation.UserInfo"/>), handed to it by
/// <see cref="BearerSchemeRegistration"/> with the settings and key the scheme is registered with.
/// It holds no rule of its own. The handler is the one JwtBearer uses, without claim mapping.
/// </summary>
public sealed class ApplicationAccessTokenValidator : IApplicationAccessTokenValidator
{
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly TokenValidationParameters _profile;

    public ApplicationAccessTokenValidator(TokenValidationParameters userInfoProfile)
    {
        _profile = userInfoProfile;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ClaimsPrincipal>> ValidateAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return AuthErrors.InvalidToken;
        }

        // Reports a bad token in the result rather than throwing, whatever is wrong with it.
        var result = await _handler.ValidateTokenAsync(token, _profile);
        return result.IsValid ? new ClaimsPrincipal(result.ClaimsIdentity) : AuthErrors.InvalidToken;
    }
}
