using System.Security.Claims;
using System.Text.Encodings.Web;
using Auth.Sdk.Models;
using Auth.Sdk.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Sdk.Handlers;

/// <summary>
/// Authentication handler that validates API keys via the AuthSystem.
/// Extracts the key from the X-Api-Key header.
/// </summary>
/// <remarks>
/// SDK 2.0: a key the AuthSystem calls invalid, a malformed one, or one presented from an address
/// outside its list is a 401, with the same message for each. A key that could not be checked is
/// a 503 with <c>Retry-After</c> (<see cref="UnavailableAnswer"/>).
/// </remarks>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private const string InvalidKey = "Invalid API key.";

    private readonly AuthSystemClient _authSystemClient;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthSystemClient authSystemClient)
        : base(options, logger, encoder)
    {
        _authSystemClient = authSystemClient;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthSystemConstants.ApiKeyHeaderName, out var apiKeyHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = apiKeyHeader.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.Fail("API key is empty.");
        }

        var check = await _authSystemClient.ValidateApiKeyAsync(apiKey, Context.RequestAborted);
        if (check.Status == CredentialCheckStatus.Unavailable)
        {
            UnavailableAnswer.Arrange(Context, check.RetryAfter ?? AuthSystemClient.DefaultRetryAfter);
            return AuthenticateResult.Fail("The API key could not be checked; try again later.");
        }

        var result = check.Result;
        if (check.Status != CredentialCheckStatus.Valid || result is null)
        {
            return AuthenticateResult.Fail(InvalidKey);
        }

        // The connection's address, never a forwarding header: behind a proxy, the application
        // configures forwarded headers for its own trusted proxy. No hint in the answer.
        if (!AllowedIpMatcher.IsAllowed(result.AllowedIps, Context.Connection.RemoteIpAddress))
        {
            Logger.LogWarning("API key {ApiKeyId} presented from an address outside its list", result.ApiKeyId);
            return AuthenticateResult.Fail(InvalidKey);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.ApiKeyId.ToString()),
            new("apikey_id", result.ApiKeyId.ToString()),
            new("application_id", result.ApplicationId.ToString()),
            new("apikey_name", result.Name),
            new("environment", result.Environment)
        };

        foreach (var scope in result.Scopes)
        {
            claims.Add(new Claim("scope", scope));
            claims.Add(new Claim("permission", scope));
        }

        var identity = new ClaimsIdentity(claims, AuthSystemConstants.ApiKeyScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthSystemConstants.ApiKeyScheme);

        return AuthenticateResult.Success(ticket);
    }
}

/// <summary>
/// Options for the API Key authentication handler.
/// </summary>
public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}
