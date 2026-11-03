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
/// <para>
/// Validating with the scheme's parameters, not a copy of them (S12), is what makes a Healthy
/// answer mean "a token signed now is accepted by every validator", whichever keys the set holds.
/// </para>
/// <para>
/// The current key changes when a promote runs in this process (S12), so a success is kept for
/// the key it was computed with, by its <c>kid</c>: the next readiness call after a promote probes
/// the new key, and an anonymous <c>/ready</c> caller still makes the server sign at most once per
/// key. A failure is never kept, so a key that recovers (or is replaced) reads Healthy at the next
/// call instead of after a restart (OI-265 (b)).
/// </para>
/// </remarks>
public sealed class SigningKeyProbe : ISigningKeyProbe
{
    private readonly JwtSettings _settings;
    private readonly ISigningKeyMaterial _signingKeys;
    private readonly Lock _gate = new();
    private string? _succeededFor;

    public SigningKeyProbe(JwtSettings settings, ISigningKeyMaterial signingKeys)
    {
        _settings = settings;
        _signingKeys = signingKeys;
    }

    /// <inheritdoc />
    public ErrorOr<Success> ProbeSigningKey()
    {
        var current = _signingKeys.Current;
        var kid = current.Key.KeyId;
        if (kid is not null && string.Equals(Volatile.Read(ref _succeededFor), kid, StringComparison.Ordinal))
        {
            return Result.Success;
        }

        // One probe at a time: callers arriving together for a key not yet proven wait for one
        // signature instead of each making one.
        lock (_gate)
        {
            if (kid is not null && string.Equals(_succeededFor, kid, StringComparison.Ordinal))
            {
                return Result.Success;
            }

            var result = SignAndValidate(_settings, _signingKeys, current);
            if (!result.IsError)
            {
                Volatile.Write(ref _succeededFor, kid);
            }

            return result;
        }
    }

    private static ErrorOr<Success> SignAndValidate(JwtSettings settings, ISigningKeyMaterial signingKeys, SigningCredentials current)
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
            SigningCredentials = current
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
