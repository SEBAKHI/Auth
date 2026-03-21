using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Sdk.Handlers;

/// <summary>
/// Authentication handler that validates webhook keys via the AuthSystem.
/// Extracts the key from the ?whk= query parameter.
/// </summary>
public class WebhookKeyAuthenticationHandler : AuthenticationHandler<WebhookKeyAuthenticationOptions>
{
    private readonly AuthSystemClient _authSystemClient;

    public WebhookKeyAuthenticationHandler(
        IOptionsMonitor<WebhookKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthSystemClient authSystemClient)
        : base(options, logger, encoder)
    {
        _authSystemClient = authSystemClient;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Query.TryGetValue(AuthSystemConstants.WebhookKeyQueryParam, out var webhookKeyValue))
        {
            return AuthenticateResult.NoResult();
        }

        var webhookKey = webhookKeyValue.ToString();
        if (string.IsNullOrWhiteSpace(webhookKey))
        {
            return AuthenticateResult.Fail("Webhook key is empty.");
        }

        if (!Request.IsHttps)
        {
            Logger.LogWarning("Webhook key transmitted over non-HTTPS connection. This is a security risk.");
        }

        var result = await _authSystemClient.ValidateWebhookKeyAsync(webhookKey, Context.RequestAborted);
        if (result is null || !result.Active)
        {
            return AuthenticateResult.Fail("Invalid webhook key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.WebhookKeyId.ToString()),
            new("webhookkey_id", result.WebhookKeyId.ToString()),
            new("application_id", result.ApplicationId.ToString()),
            new("webhookkey_name", result.Name),
            new("target_url", result.TargetUrl),
            new("environment", result.Environment)
        };

        var identity = new ClaimsIdentity(claims, AuthSystemConstants.WebhookKeyScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthSystemConstants.WebhookKeyScheme);

        return AuthenticateResult.Success(ticket);
    }
}

/// <summary>
/// Options for the Webhook Key authentication handler.
/// </summary>
public class WebhookKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}
