using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Sdk.TokenManagement;

/// <summary>
/// <see cref="IMachineTokenProvider"/> over the AuthSystem's token endpoint (OI-128).
/// </summary>
/// <remarks>
/// <para>
/// Each token request carries a new assertion: header <c>alg ES256</c>, the key's <c>kid</c> and
/// <c>typ client-authentication+jwt</c>; claims <c>iss</c> = <c>sub</c> = the client id,
/// <c>aud</c> = the AuthSystem's issuer identifier (and nothing else), a random <c>jti</c>,
/// <c>iat</c>, and an <c>exp</c> one minute later. The AuthSystem accepts each assertion once.
/// </para>
/// <para>
/// The request goes through its own named client, which carries the gateway token and no
/// <see cref="TokenRefreshHandler"/>: a user's bearer token must never ride along on it.
/// </para>
/// <para>
/// No automatic retry. A failure is thrown as a <see cref="MachineTokenException"/> that says
/// whether it is transient, and the next call simply asks again with a new assertion.
/// </para>
/// </remarks>
public sealed class MachineTokenProvider : IMachineTokenProvider, IDisposable
{
    /// <summary>How long a signed assertion is valid.</summary>
    public static readonly TimeSpan AssertionLifetime = TimeSpan.FromSeconds(60);

    /// <summary>A cached token is replaced this long before it expires.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    /// <summary>The assertion's <c>typ</c> header (draft-ietf-oauth-rfc7523bis).</summary>
    public const string AssertionTokenType = "client-authentication+jwt";

    /// <summary>RFC 7523 §2.2.</summary>
    public const string JwtBearerAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthSystemOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<MachineTokenProvider> _logger;
    private readonly object _sync = new();
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    private ECDsa? _privateKey;
    private SigningCredentials? _signingCredentials;
    private volatile CachedToken? _cached;

    /// <summary>The token request in flight, shared by every caller that arrives meanwhile.</summary>
    private Task<CachedToken>? _inflight;

    public MachineTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AuthSystemOptions> options,
        TimeProvider time,
        ILogger<MachineTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (TryCached(out var token))
        {
            return token;
        }

        // Single flight, for a failure as much as for a success: every caller that arrives while
        // a request is in flight awaits that one request and shares its token or its exception.
        // Queued callers sending a request each would multiply an outage's timeouts, and spend
        // the token endpoint's per-address rate limit, which this server's user sign-ins share.
        Task<CachedToken> request;
        lock (_sync)
        {
            if (TryCached(out token))
            {
                return token;
            }

            if (_inflight is { } running)
            {
                request = running;
            }
            else
            {
                // Not the caller's token: one caller giving up must not cancel the request the
                // others wait on. The machine client's own timeout bounds it.
                var started = RequestTokenAsync(CancellationToken.None);
                _inflight = started;
                // A request that finished synchronously (a configuration error) settles right
                // here, inside this lock, which is re-entrant: `started` stays the one to await.
                _ = started.ContinueWith(Settle, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                request = started;
            }
        }

        var fresh = await request.WaitAsync(cancellationToken);
        return fresh.AccessToken;
    }

    /// <summary>Caches a finished request's token, and frees the slot for the next request.</summary>
    private void Settle(Task<CachedToken> finished)
    {
        lock (_sync)
        {
            if (finished.IsCompletedSuccessfully)
            {
                _cached = finished.Result;
            }

            if (ReferenceEquals(_inflight, finished))
            {
                _inflight = null;
            }
        }
    }

    /// <summary>
    /// A new signed client assertion, valid for <see cref="AssertionLifetime"/> from now.
    /// </summary>
    private string CreateAssertion()
    {
        var credentials = SigningCredentials();
        var now = _time.GetUtcNow().UtcDateTime;

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = AssertionTokenType,
            Issuer = _options.ClientId,
            // The issuer identifier, the one audience the AuthSystem accepts: never the token
            // endpoint's URL, and never a second value.
            Audience = _options.Issuer,
            IssuedAt = now,
            Expires = now.Add(AssertionLifetime),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = _options.ClientId!,
                ["jti"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            },
            SigningCredentials = credentials
        });
    }

    private bool TryCached(out string token)
    {
        var cached = _cached;
        if (cached is not null && _time.GetUtcNow() < cached.ExpiresAt - RefreshMargin)
        {
            token = cached.AccessToken;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<CachedToken> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var assertion = CreateAssertion();
        var client = _httpClientFactory.CreateClient(AuthSystemConstants.MachineHttpClientName);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_assertion_type"] = JwtBearerAssertionType,
            ["client_assertion"] = assertion,
            ["client_id"] = _options.ClientId!,
        });

        // The expiry is counted from before the request: the token cannot be older than that.
        var sentAt = _time.GetUtcNow();
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(AuthSystemConstants.TokenEndpointPath, form, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // No answer: a network failure, or the client's own timeout (not the caller's cancel).
            _logger.LogWarning(ex, "Machine token request to the AuthSystem got no answer");
            throw new MachineTokenException("The AuthSystem could not be reached for a machine token.", null, null, null, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorCode = await ReadProblemCodeAsync(response, cancellationToken);
                var retryAfter = response.Headers.RetryAfter?.Delta;
                _logger.LogWarning(
                    "Machine token request refused with {StatusCode} {ErrorCode}", (int)response.StatusCode, errorCode);
                throw new MachineTokenException(
                    $"The AuthSystem refused the machine token request ({(int)response.StatusCode} {errorCode}).",
                    response.StatusCode, errorCode, retryAfter);
            }

            TokenResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new MachineTokenException(
                    "The AuthSystem answered the machine token request with a body that is not a token response.",
                    response.StatusCode, null, null, ex);
            }

            if (body is null || string.IsNullOrEmpty(body.AccessToken) || body.ExpiresIn <= 0)
            {
                throw new MachineTokenException(
                    "The AuthSystem answered the machine token request without a token.", response.StatusCode, null, null);
            }

            _logger.LogDebug("Machine token obtained, valid for {ExpiresIn} s", body.ExpiresIn);
            return new CachedToken(body.AccessToken, sentAt.AddSeconds(body.ExpiresIn));
        }
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The signing credentials, built once from the configured PEM. A missing option or a key that
    /// is not P-256 is a configuration error, reported as such.
    /// </summary>
    private SigningCredentials SigningCredentials()
    {
        if (_signingCredentials is not null)
        {
            return _signingCredentials;
        }

        if (!_options.IsMachineClientConfigured)
        {
            throw new InvalidOperationException(
                "The machine client needs AuthSystemOptions.ClientId, MachineKeyId, MachinePrivateKeyPem, BaseUrl and Issuer.");
        }

        var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(_options.MachinePrivateKeyPem);
            var curve = ecdsa.ExportParameters(includePrivateParameters: false).Curve;
            if (ecdsa.KeySize != 256 || curve.Oid?.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                throw new InvalidOperationException("The machine private key must be a P-256 key.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException("MachinePrivateKeyPem is not a readable P-256 private key in PEM.", ex);
        }
        catch
        {
            ecdsa.Dispose();
            throw;
        }

        _privateKey = ecdsa;
        _signingCredentials = new SigningCredentials(
            new ECDsaSecurityKey(ecdsa) { KeyId = _options.MachineKeyId },
            SecurityAlgorithms.EcdsaSha256);
        return _signingCredentials;
    }

    public void Dispose()
    {
        _privateKey?.Dispose();
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
