using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Sdk.Handlers;

/// <summary>
/// Authentication handler that validates API keys via the AuthSystem.
/// Extracts the key from the X-Api-Key header.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
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

        var result = await _authSystemClient.ValidateApiKeyAsync(apiKey, Context.RequestAborted);
        if (result is null || !result.Active)
        {
            return AuthenticateResult.Fail("Invalid API key.");
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
