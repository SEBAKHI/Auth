using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Google's token endpoint, called once per Google sign-in to exchange the authorization code
/// for the ID token (<see cref="IGoogleCodeExchanger"/>).
/// </summary>
/// <remarks>
/// <para>
/// One attempt, no retry: the code is single-use, and a second request after a lost answer
/// would be refused by Google anyway. The typed client's timeout (<see cref="Timeout"/>) bounds
/// how long a sign-in waits; past it, or when Google is unreachable or answers 5xx, the
/// exception reaches the global handler, which answers 503 <c>Http.Unavailable</c>.
/// </para>
/// <para>
/// Nothing secret is ever logged: not the code, the verifier, the client secret or any token.
/// A refusal logs Google's <c>error</c> value (an RFC 6749 §5.2 code such as
/// <c>invalid_grant</c>) and the status, and nothing else from the answer.
/// </para>
/// </remarks>
public sealed partial class GoogleCodeExchanger : IGoogleCodeExchanger
{
    /// <summary>Google's OAuth 2.0 token endpoint.</summary>
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>How long one exchange may take, set on the typed client.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>The largest answer read: Google's is about two kilobytes.</summary>
    public const int MaxResponseBytes = 64 * 1024;

    /// <summary>
    /// Google's refusals that mean this deployment's own configuration is wrong (the client id
    /// or secret, or a callback address missing from the client), not the person's code.
    /// They are answered as <c>ProviderNotConfigured</c> and logged at Error for the operator.
    /// </summary>
    private static readonly FrozenSet<string> ConfigurationFaults =
        new[] { "invalid_client", "unauthorized_client", "redirect_uri_mismatch" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<ExternalAuthSettings> _settings;
    private readonly ILogger<GoogleCodeExchanger> _logger;

    public GoogleCodeExchanger(
        HttpClient httpClient,
        IOptionsMonitor<ExternalAuthSettings> settings,
        ILogger<GoogleCodeExchanger> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<string>> ExchangeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var google = _settings.CurrentValue.Google;
        if (google is null ||
            !google.Enabled ||
            string.IsNullOrWhiteSpace(google.ClientId) ||
            string.IsNullOrWhiteSpace(google.ClientSecret))
        {
            _logger.LogWarning(
                "Google code exchange refused before calling Google: sign-in enabled {Enabled}, client id set {HasClientId}, client secret set {HasClientSecret}",
                google?.Enabled ?? false,
                !string.IsNullOrWhiteSpace(google?.ClientId),
                !string.IsNullOrWhiteSpace(google?.ClientSecret));
            return ExternalAuthErrors.ProviderNotConfigured("google");
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = google.ClientId,
            ["client_secret"] = google.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        });

        using var response = await _httpClient.PostAsync(TokenEndpoint, form, cancellationToken);
        var status = (int)response.StatusCode;

        if (status >= 500)
        {
            _logger.LogWarning("Google's token endpoint answered {StatusCode}", status);
            throw new HttpRequestException(
                $"Google's token endpoint answered {status}.", inner: null, response.StatusCode);
        }

        var answer = await ReadAnswerAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode && (answer is null || status == 429))
        {
            // Not an OAuth refusal: Google throttling this server (429), or an answer that is not
            // Google's JSON at all (an outbound proxy's error page). Neither is the person's code,
            // so it is an outage, not "try again": 503 with Retry-After, and a line the operator
            // can find.
            answer?.Dispose();
            _logger.LogWarning(
                "Google's token endpoint answered {StatusCode} without an OAuth error body; treated as unavailable", status);
            throw new HttpRequestException(
                $"Google's token endpoint answered {status} without an OAuth error.", inner: null, response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = ErrorValue(answer);
            if (ConfigurationFaults.Contains(error))
            {
                _logger.LogError(
                    "Google refused the code exchange with {GoogleError} ({StatusCode}): check the Google client id, its secret, and that this app's callback address is listed on the client",
                    error, status);
                return ExternalAuthErrors.ProviderNotConfigured("google");
            }

            _logger.LogWarning(
                "Google refused the code exchange with {GoogleError} ({StatusCode})", error, status);
            return ExternalAuthErrors.CodeExchangeFailed;
        }

        if (answer is not null &&
            answer.RootElement.ValueKind == JsonValueKind.Object &&
            answer.RootElement.TryGetProperty("id_token", out var idToken) &&
            idToken.ValueKind == JsonValueKind.String &&
            idToken.GetString() is { Length: > 0 } value)
        {
            answer.Dispose();
            return value;
        }

        answer?.Dispose();
        // A success without an ID token: the openid scope was not granted, or the answer is not
        // Google's. Nothing to sign in with, and nothing a retry of this code would change.
        _logger.LogWarning("Google's token endpoint answered {StatusCode} without an ID token", status);
        return ExternalAuthErrors.CodeExchangeFailed;
    }

    /// <summary>The answer as JSON, or null when it is not JSON (a proxy's error page).</summary>
    private static async Task<JsonDocument?> ReadAnswerAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Google's <c>error</c> value, reduced to the RFC 6749 shape before it reaches a log line;
    /// anything else reads "unrecognized".
    /// </summary>
    private static string ErrorValue(JsonDocument? answer)
    {
        using (answer)
        {
            var value = answer is not null &&
                        answer.RootElement.ValueKind == JsonValueKind.Object &&
                        answer.RootElement.TryGetProperty("error", out var error) &&
                        error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;

            return value is not null && ErrorCodeShape().IsMatch(value) ? value : "unrecognized";
        }
    }

    [GeneratedRegex("^[a-z_]{1,64}$")]
    private static partial Regex ErrorCodeShape();
}
