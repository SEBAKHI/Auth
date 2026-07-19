using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Authorize;

/// <summary>
/// Handles the OAuth 2.0 authorize request. Per RFC 6749 §4.1.2.1, failures are
/// split in two: an unknown client or unregistered redirect_uri returns an
/// error WITHOUT redirecting (the redirect target cannot be trusted), while all
/// later failures redirect back to the validated redirect_uri with an OAuth
/// error code. Without a valid IdP session the browser is sent to the accounts
/// login page carrying the original authorize URL as returnTo.
/// </summary>
public class AuthorizeCommandHandler : IRequestHandler<AuthorizeCommand, ErrorOr<AuthorizeResult>>
{
    // RFC 7636: base64url characters, 43 chars for an S256 challenge
    // (SHA-256 output) with headroom up to the 128 the spec allows verifiers.
    private static readonly Regex CodeChallengePattern = new(
        "^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private const int MaxStateLength = 512;

    private readonly IApplicationRepository _applicationRepository;
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IAuthorizationCodeRepository _authorizationCodeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<AuthorizeCommandHandler> _logger;

    public AuthorizeCommandHandler(
        IApplicationRepository applicationRepository,
        IIdpSessionRepository idpSessionRepository,
        IAuthorizationCodeRepository authorizationCodeRepository,
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IOptions<IdentityProviderSettings> idpSettings,
        ILogger<AuthorizeCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _idpSessionRepository = idpSessionRepository;
        _authorizationCodeRepository = authorizationCodeRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _idpSettings = idpSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<AuthorizeResult>> Handle(AuthorizeCommand request, CancellationToken cancellationToken)
    {
        // --- Hard failures: no redirect target may be trusted yet ---

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return AuthErrors.InvalidClient;
        }

        var application = await _applicationRepository.GetByCodeAsync(request.ClientId, cancellationToken);
        if (application is null || !application.IsActive)
        {
            _logger.LogWarning("Authorize request for unknown or inactive client {ClientId}", request.ClientId);
            return AuthErrors.InvalidClient;
        }

        if (string.IsNullOrWhiteSpace(request.RedirectUri) ||
            !application.IsRedirectUriAllowed(request.RedirectUri))
        {
            _logger.LogWarning(
                "Authorize request for client {ClientId} with unregistered redirect_uri", request.ClientId);
            return AuthErrors.InvalidRedirectUri;
        }

        // --- Soft failures: redirect back to the validated redirect_uri ---

        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
        {
            return ErrorRedirect(request, "unsupported_response_type");
        }

        if (request.State is { Length: > MaxStateLength })
        {
            return ErrorRedirect(request, "invalid_request");
        }

        if (string.IsNullOrWhiteSpace(request.CodeChallenge) ||
            !CodeChallengePattern.IsMatch(request.CodeChallenge))
        {
            return ErrorRedirect(request, "invalid_request");
        }

        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal))
        {
            return ErrorRedirect(request, "invalid_request");
        }

        // --- IdP session: no valid session means the browser goes to login ---

        var session = await ResolveSessionAsync(request.IdpSessionToken, cancellationToken);
        if (session is null)
        {
            return new AuthorizeResult
            {
                RedirectUrl = BuildLoginRedirect(request.OriginalRequestUrl),
                IsLoginRedirect = true
            };
        }

        var user = await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || user.IsLockedOut())
        {
            // Force a fresh interactive login instead of leaking account state.
            return new AuthorizeResult
            {
                RedirectUrl = BuildLoginRedirect(request.OriginalRequestUrl),
                IsLoginRedirect = true
            };
        }

        // --- Issue the one-time code bound to this exact request ---

        var plainCode = _jwtTokenService.GenerateRefreshToken();
        var code = AuthorizationCode.Create(
            application.Id,
            user.Id,
            _refreshTokenKeyService.ComputeTokenHash(plainCode),
            request.RedirectUri,
            request.CodeChallenge,
            _idpSettings.AuthorizationCodeLifetime,
            request.IpAddress);

        await _authorizationCodeRepository.CreateAsync(code, cancellationToken);

        _logger.LogInformation(
            "Issued authorization code for client {ClientId} and user {UserId}",
            request.ClientId, user.Id);

        return new AuthorizeResult
        {
            RedirectUrl = AppendQuery(request.RedirectUri, "code", plainCode, request.State)
        };
    }

    private async Task<IdpSession?> ResolveSessionAsync(string? idpSessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idpSessionToken))
        {
            return null;
        }

        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(idpSessionToken);
        var session = await _idpSessionRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        return session is not null && session.IsValid() ? session : null;
    }

    private string BuildLoginRedirect(string originalRequestUrl)
    {
        var accountsBase = _idpSettings.AccountsBaseUrl.TrimEnd('/');
        return $"{accountsBase}/login?returnTo={Uri.EscapeDataString(originalRequestUrl)}";
    }

    private static AuthorizeResult ErrorRedirect(AuthorizeCommand request, string oauthError)
    {
        return new AuthorizeResult
        {
            RedirectUrl = AppendQuery(request.RedirectUri!, "error", oauthError, request.State)
        };
    }

    private static string AppendQuery(string redirectUri, string key, string value, string? state)
    {
        var separator = redirectUri.Contains('?') ? '&' : '?';
        var url = $"{redirectUri}{separator}{key}={Uri.EscapeDataString(value)}";

        if (!string.IsNullOrEmpty(state))
        {
            url += $"&state={Uri.EscapeDataString(state)}";
        }

        return url;
    }
}
