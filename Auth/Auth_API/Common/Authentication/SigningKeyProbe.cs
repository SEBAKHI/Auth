using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Infrastructure.Authentication;
using ErrorOr;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Common.Authentication;

/// <summary>
/// The readiness probe (X03): signs a short-lived token with the key every access token is signed
/// with now, and validates it with the platform scheme's own parameters
/// (<see cref="AccessTokenValidation.Platform"/>). Never issued to anyone.
/// </summary>
/// <remarks>
/// Validating with the scheme's parameters, not a copy of them (S12), is what makes a Healthy
/// answer mean "a token signed now is accepted by every validator", whichever keys the set holds.
/// The set holds one fixed key for the life of the process, so the answer is computed once, on the
/// first readiness call: an anonymous <c>/ready</c> caller never makes the server sign again.
/// </remarks>
public sealed class SigningKeyProbe : ISigningKeyProbe
{
    private readonly Lazy<ErrorOr<Success>> _result;

    public SigningKeyProbe(JwtSettings settings, ISigningKeyMaterial signingKeys)
    {
        _result = new Lazy<ErrorOr<Success>>(
            () => SignAndValidate(settings, signingKeys),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public ErrorOr<Success> ProbeSigningKey() => _result.Value;

    private static ErrorOr<Success> SignAndValidate(JwtSettings settings, ISigningKeyMaterial signingKeys)
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var probe = handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtClaimNames.Subject, "signing-key-probe")]),
            Expires = now.AddMinutes(1),
            NotBefore = now,
            IssuedAt = now,
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            SigningCredentials = signingKeys.Current
        }));

        try
        {
            handler.ValidateToken(probe, AccessTokenValidation.Platform(settings, signingKeys), out _);
            return Result.Success;
        }
        catch (SecurityTokenExpiredException)
        {
            return AuthErrors.TokenExpired;
        }
        catch (SecurityTokenException)
        {
            return AuthErrors.InvalidToken;
        }
    }
}
