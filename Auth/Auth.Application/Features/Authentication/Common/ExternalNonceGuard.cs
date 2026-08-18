using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The one rule deciding whether a provider sign-in's nonce is trustworthy,
/// shared by every anonymous endpoint that accepts a provider ID token.
/// </summary>
/// <remarks>
/// Two endpoints take a provider token from an unauthenticated caller — sign-in
/// and pending-deletion recovery — and a check that guarded only one of them
/// would simply move the way in rather than close it.
/// <para>
/// What is actually being established: that the nonce sealed inside the token was
/// issued by THIS server to THIS browser. A browser-generated value cannot show
/// that, because the same request carries both the token and the value it is
/// compared against — whoever holds a stolen token reads the nonce out of it and
/// sends the matching value. Pairing the value with a cookie only this browser
/// holds is what a replayer cannot reproduce.
/// </para>
/// </remarks>
public class ExternalNonceGuard
{
    private readonly IRefreshTokenKeyService _tokenKeyService;
    private readonly IOptionsMonitor<ExternalAuthSettings> _settings;

    public ExternalNonceGuard(
        IRefreshTokenKeyService tokenKeyService,
        IOptionsMonitor<ExternalAuthSettings> settings)
    {
        _tokenKeyService = tokenKeyService;
        _settings = settings;
    }

    /// <summary>
    /// Checks the presented nonce against the browser's cookie.
    /// </summary>
    /// <param name="nonce">The value the caller says the token was minted with.</param>
    /// <param name="nonceCookie">The hash this server stored when it issued one.</param>
    /// <returns>
    /// Success when the pair holds, or when enforcement is off. The provider still
    /// compares the value to the token's own claim afterwards; this only
    /// establishes where the value came from.
    /// </returns>
    public ErrorOr<Success> Validate(string? nonce, string? nonceCookie)
    {
        // Read per call, so the rollout switch can be turned on — or straight back
        // off during an incident — without a restart.
        if (!_settings.CurrentValue.RequireNonce)
        {
            return Result.Success;
        }

        if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(nonceCookie))
        {
            return ExternalAuthErrors.NonceRequired;
        }

        var expected = _tokenKeyService.ComputeTokenHash(nonce);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(nonceCookie))
            ? Result.Success
            : ExternalAuthErrors.NonceRequired;
    }
}
