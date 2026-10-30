using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.Sdk.Extensions;
using Auth.Sdk.Models;
using Auth.Sdk.TokenManagement;
using Auth.Sdk.Validation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Sdk;

/// <summary>
/// HTTP client for communicating with the AuthSystem API.
/// Includes response caching to minimize remote validation calls
/// and transparent token lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// SDK 2.0 (OI-128 PR B): the credential checks (API key, webhook key, introspection) ask as this
/// application, with its machine token, and the AuthSystem answers only about this application's
/// own credentials. They never send a user's token from the token store.
/// </para>
/// <para>
/// A check has three outcomes (<see cref="CredentialCheck{TResult}"/>): valid, invalid, or
/// unavailable. Only the AuthSystem's own "invalid" makes a key invalid; throttling, an outage, a
/// timeout, or a refusal of this application's machine credential is "unavailable", which the
/// schemes answer with 503 and <c>Retry-After</c>, and which is never cached.
/// </para>
/// <para>
/// Results are cached in caches this client owns, never the host's <c>IMemoryCache</c>: a host
/// cache with a <c>SizeLimit</c> throws on every entry without a size, and a negative cache inside
/// an unbounded host cache is a memory flood. A valid result is kept for the configured duration,
/// an invalid one for <see cref="InvalidResultCacheDuration"/>, each cache bounded to
/// <see cref="CacheSizeLimit"/> entries. A valid result is never kept past its time because the
/// AuthSystem is unreachable: that would keep a revoked key alive.
/// </para>
/// </remarks>
public sealed class AuthSystemClient : IDisposable
{
    /// <summary>How long a key the AuthSystem called invalid is answered locally.</summary>
    public static readonly TimeSpan InvalidResultCacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>The most entries each of the two result caches holds.</summary>
    public const int CacheSizeLimit = 10_000;

    /// <summary>The <c>Retry-After</c> of an unavailable answer when the AuthSystem named none.</summary>
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(5);

    private const string InvalidApiKeyCode = "ApiKey.Invalid";
    private const string InvalidWebhookKeyCode = "WebhookKey.Invalid";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenStore _tokenStore;
    private readonly AuthSystemOptions _options;
    private readonly ILogger<AuthSystemClient> _logger;
    private readonly IMachineTokenProvider _machineTokens;
    private readonly TimeProvider _time;
    private readonly MemoryCache _validResults = new(new MemoryCacheOptions { SizeLimit = CacheSizeLimit });
    private readonly MemoryCache _invalidResults = new(new MemoryCacheOptions { SizeLimit = CacheSizeLimit });

    // The credential checks in flight, one per cache key; an entry lives only as long as its request.
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// The whole budget of one check: <see cref="ServiceCollectionExtensions.CredentialCheckTimeout"/>.
    /// Settable for the tests, which cannot wait ten seconds.
    /// </summary>
    internal TimeSpan CheckBudget { get; set; } = ServiceCollectionExtensions.CredentialCheckTimeout;

    public AuthSystemClient(
        IHttpClientFactory httpClientFactory,
        ITokenStore tokenStore,
        IOptions<AuthSystemOptions> options,
        ILogger<AuthSystemClient> logger,
        IMachineTokenProvider machineTokens,
        TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;
        _machineTokens = machineTokens;
        _time = time;
    }

    /// <summary>
    /// This application's machine access token (OI-128), obtained with its machine key and cached
    /// until a minute before it expires. Needs <see cref="AuthSystemOptions.ClientId"/>,
    /// <see cref="AuthSystemOptions.MachineKeyId"/> and <see cref="AuthSystemOptions.MachinePrivateKeyPem"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The machine client is not configured.</exception>
    /// <exception cref="MachineTokenException">The AuthSystem did not issue a token; see <see cref="MachineTokenException.IsTransient"/>.</exception>
    public Task<string> GetMachineAccessTokenAsync(CancellationToken cancellationToken = default) =>
        _machineTokens.GetAccessTokenAsync(cancellationToken);

    /// <summary>
    /// Validates an API key presented to this application. A malformed key is invalid without a
    /// call; results are cached (a valid one for <see cref="AuthSystemOptions.ApiKeyCacheDuration"/>,
    /// an invalid one for <see cref="InvalidResultCacheDuration"/>).
    /// </summary>
    public async Task<CredentialCheck<ApiKeyValidationResult>> ValidateApiKeyAsync(string rawApiKey, CancellationToken cancellationToken = default)
    {
        if (!ApiKeyShape.IsWellFormed(rawApiKey))
        {
            return CredentialCheck<ApiKeyValidationResult>.Invalid();
        }

        return await CheckAsync(
            $"apikey:{ComputeCacheKey(rawApiKey)}",
            AuthSystemConstants.ApiKeyValidationPath,
            () => JsonContent.Create(new { apiKey = rawApiKey }),
            InvalidApiKeyCode,
            _options.ApiKeyCacheDuration,
            (ApiKeyValidationResult result) => (result.Active, result.ApplicationCode),
            cancellationToken);
    }

    /// <summary>
    /// Validates a webhook key presented to this application. Results are cached (a valid one for
    /// <see cref="AuthSystemOptions.WebhookKeyCacheDuration"/>, an invalid one for
    /// <see cref="InvalidResultCacheDuration"/>).
    /// </summary>
    public async Task<CredentialCheck<WebhookKeyValidationResult>> ValidateWebhookKeyAsync(string rawWebhookKey, CancellationToken cancellationToken = default)
    {
        // Every webhook key starts so; anything else is invalid without a call.
        if (string.IsNullOrWhiteSpace(rawWebhookKey) || !rawWebhookKey.StartsWith("wk_", StringComparison.Ordinal))
        {
            return CredentialCheck<WebhookKeyValidationResult>.Invalid();
        }

        return await CheckAsync(
            $"webhookkey:{ComputeCacheKey(rawWebhookKey)}",
            AuthSystemConstants.WebhookKeyValidationPath,
            () => JsonContent.Create(new { webhookKey = rawWebhookKey }),
            InvalidWebhookKeyCode,
            _options.WebhookKeyCacheDuration,
            (WebhookKeyValidationResult result) => (result.Active, result.ApplicationCode),
            cancellationToken);
    }

    /// <summary>
    /// Introspects a token (RFC 7662), as this application: <c>active</c> only for an access or
    /// refresh token issued to this application and still valid. Null when no answer could be had
    /// (the AuthSystem was throttling, failing or unreachable, or refused this application's
    /// machine credential). An inactive answer is held for <see cref="InvalidResultCacheDuration"/>,
    /// since an inactive token never becomes active again; an active one is never held, because the
    /// point of asking is to hear a sign-out at once. Introspection counts on the same budget at
    /// the AuthSystem as the key checks.
    /// </summary>
    public async Task<TokenIntrospectionResult?> IntrospectTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"introspect:{ComputeCacheKey(token)}";
        if (TryCached(_invalidResults, cacheKey, out bool _))
        {
            return new TokenIntrospectionResult { Active = false };
        }

        // Concurrent introspections of one token share one request, as the key checks do.
        return await Shared(cacheKey, () => AskIntrospectionAsync(cacheKey, token)).WaitAsync(cancellationToken);
    }

    private async Task<TokenIntrospectionResult?> AskIntrospectionAsync(string cacheKey, string token)
    {
        // Form-encoded, not JSON: RFC 7662 defines the introspection request as
        // application/x-www-form-urlencoded, and the endpoint enforces it.
        var answer = await SendAsMachineAsync(
            AuthSystemConstants.TokenIntrospectionPath,
            () => new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
        using var response = answer.Response;

        if (response is not { IsSuccessStatusCode: true })
        {
            _logger.LogWarning("Token introspection had no answer (status {StatusCode})", response?.StatusCode);
            return null;
        }

        TokenIntrospectionResult? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<TokenIntrospectionResult>(CancellationToken.None);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Token introspection answered with an unreadable body");
            return null;
        }

        if (result is { Active: false })
        {
            Remember(_invalidResults, cacheKey, true, InvalidResultCacheDuration);
        }

        return result;
    }

    /// <summary>
    /// Authenticates with the AuthSystem and stores tokens for auto-refresh.
    /// Subsequent HTTP requests via the SDK will automatically include and refresh the token.
    /// </summary>
    /// <param name="email">User email address.</param>
    /// <param name="password">User password.</param>
    /// <param name="applicationId">Target application ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if login succeeded, false otherwise.</returns>
    public async Task<bool> LoginAsync(string email, string password, string applicationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password, applicationId },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SDK login failed with status {StatusCode}", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResult>(cancellationToken);
            if (result?.Token is null)
            {
                return false;
            }

            await _tokenStore.SetAsync(
                result.Token.AccessToken,
                result.Token.RefreshToken,
                result.Token.ExpiresIn,
                cancellationToken);

            _logger.LogDebug("SDK login successful — tokens stored for auto-refresh");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SDK login failed");
            return false;
        }
    }

    /// <summary>
    /// Stores externally obtained tokens for the SDK's auto-refresh mechanism.
    /// Use this when tokens are obtained outside the SDK (e.g., from a frontend login flow).
    /// </summary>
    public async Task SetTokensAsync(string accessToken, string refreshToken, int expiresInSeconds, CancellationToken cancellationToken = default)
    {
        await _tokenStore.SetAsync(accessToken, refreshToken, expiresInSeconds, cancellationToken);
    }

    /// <summary>
    /// Clears stored tokens, effectively logging out the SDK client.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _tokenStore.ClearAsync(cancellationToken);
    }

    public void Dispose()
    {
        _validResults.Dispose();
        _invalidResults.Dispose();
    }

    /// <summary>
    /// One credential check: the caches, then the AuthSystem as this application. Valid only when
    /// the answer is active and names this application; invalid only when the AuthSystem says so
    /// with <paramref name="invalidCode"/>; unavailable otherwise, and then nothing is cached.
    /// </summary>
    private async Task<CredentialCheck<TResult>> CheckAsync<TResult>(
        string cacheKey,
        string path,
        Func<HttpContent> content,
        string invalidCode,
        TimeSpan validFor,
        Func<TResult, (bool Active, string ApplicationCode)> read,
        CancellationToken cancellationToken)
        where TResult : class
    {
        if (TryCached(_validResults, cacheKey, out TResult? valid))
        {
            return CredentialCheck<TResult>.Valid(valid!);
        }

        if (TryCached(_invalidResults, cacheKey, out bool _))
        {
            return CredentialCheck<TResult>.Invalid();
        }

        // Concurrent checks of one credential wait on the request already in flight instead of
        // each spending this application's budget at the AuthSystem: a burst of one forged key
        // costs one call, as a single request does. The shared request runs without any caller's
        // cancellation (its own deadline bounds it); each caller stops waiting on its own token.
        return await Shared(cacheKey, () => AskAsync(cacheKey, path, content, invalidCode, validFor, read))
            .WaitAsync(cancellationToken);
    }

    /// <summary>
    /// The request in flight for <paramref name="cacheKey"/>, or a new one started by
    /// <paramref name="ask"/>. The entry is removed when its request ends, after the answer is cached.
    /// </summary>
    private Task<T> Shared<T>(string cacheKey, Func<Task<T>> ask)
    {
        while (true)
        {
            if (_inFlight.TryGetValue(cacheKey, out var running))
            {
                // One cache key holds one result type: the key carries the credential kind.
                return (Task<T>)running.Value;
            }

            Lazy<Task> entry = null!;
            entry = new Lazy<Task>(() => AskThenReleaseAsync(cacheKey, entry, ask), LazyThreadSafetyMode.ExecutionAndPublication);
            if (_inFlight.TryAdd(cacheKey, entry))
            {
                return (Task<T>)entry.Value;
            }
        }
    }

    private async Task<T> AskThenReleaseAsync<T>(string cacheKey, Lazy<Task> entry, Func<Task<T>> ask)
    {
        try
        {
            return await ask();
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task>>(cacheKey, entry));
        }
    }

    /// <summary>
    /// The AuthSystem's answer for one credential, cached by its outcome. Runs for every caller
    /// waiting on it, so it takes no caller's cancellation; the body it reads is already buffered.
    /// </summary>
    private async Task<CredentialCheck<TResult>> AskAsync<TResult>(
        string cacheKey,
        string path,
        Func<HttpContent> content,
        string invalidCode,
        TimeSpan validFor,
        Func<TResult, (bool Active, string ApplicationCode)> read)
        where TResult : class
    {
        var cancellationToken = CancellationToken.None;
        var answer = await SendAsMachineAsync(path, content);
        using var response = answer.Response;
        if (response is null)
        {
            return CredentialCheck<TResult>.Unavailable(answer.RetryAfter);
        }

        if (response.IsSuccessStatusCode)
        {
            TResult? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<TResult>(cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Credential check answered with an unreadable body");
                return CredentialCheck<TResult>.Unavailable(DefaultRetryAfter);
            }

            if (result is null)
            {
                return CredentialCheck<TResult>.Unavailable(DefaultRetryAfter);
            }

            var (active, applicationCode) = read(result);
            if (!active)
            {
                Remember(_invalidResults, cacheKey, true, InvalidResultCacheDuration);
                return CredentialCheck<TResult>.Invalid();
            }

            // Defence in depth: the AuthSystem answers only about this application's credentials,
            // and a result that names another application is refused all the same.
            if (!IsThisApplication(applicationCode))
            {
                _logger.LogError("Credential check refused: the result names another application");
                Remember(_invalidResults, cacheKey, true, InvalidResultCacheDuration);
                return CredentialCheck<TResult>.Invalid();
            }

            Remember(_validResults, cacheKey, result, validFor);
            return CredentialCheck<TResult>.Valid(result);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest
            && string.Equals(await ReadProblemCodeAsync(response, cancellationToken), invalidCode, StringComparison.Ordinal))
        {
            Remember(_invalidResults, cacheKey, true, InvalidResultCacheDuration);
            return CredentialCheck<TResult>.Invalid();
        }

        _logger.LogWarning("Credential check had no usable answer (status {StatusCode})", response.StatusCode);
        return CredentialCheck<TResult>.Unavailable(RetryAfterOf(response));
    }

    /// <summary>
    /// Posts to <paramref name="path"/> with this application's machine token, within one deadline,
    /// <see cref="CheckBudget"/>, for the whole check: the machine token, the send and the one
    /// renewal. Null response: no answer could be had (no token, the deadline, a network failure,
    /// a second 401), and the scheme answers 503 rather than hold the caller's request.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, TimeSpan RetryAfter)> SendAsMachineAsync(
        string path,
        Func<HttpContent> content)
    {
        using var deadline = new CancellationTokenSource(CheckBudget, _time);
        try
        {
            return await SendWithinAsync(path, content, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            _logger.LogWarning("A credential check passed its budget of {Budget}", CheckBudget);
            return (null, DefaultRetryAfter);
        }
    }

    /// <summary>
    /// <see cref="SendAsMachineAsync"/> until <paramref name="cancellationToken"/>, its deadline. A
    /// 401 means the token itself was refused (its machine key revoked or replaced inside its five
    /// minutes): the cached token is dropped and the request is sent once more with a new one.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, TimeSpan RetryAfter)> SendWithinAsync(
        string path,
        Func<HttpContent> content,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            string token;
            try
            {
                token = await _machineTokens.GetAccessTokenAsync(cancellationToken);
            }
            catch (MachineTokenException ex)
            {
                // Transient or not, the check cannot be made: a refused machine credential is this
                // application's configuration, never the presented key's fault.
                _logger.LogWarning(ex, "No machine token for a credential check (status {StatusCode})", ex.StatusCode);
                return (null, ex.RetryAfter ?? DefaultRetryAfter);
            }

            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content() };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                response = await _httpClientFactory
                    .CreateClient(AuthSystemConstants.MachineApiHttpClientName)
                    .SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A network failure, or the client's own timeout; the deadline is caught by the caller.
                _logger.LogWarning(ex, "A credential check could not reach the AuthSystem");
                return (null, DefaultRetryAfter);
            }

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return (response, RetryAfterOf(response));
            }

            response.Dispose();
            _machineTokens.Invalidate(token);
            _logger.LogWarning("The AuthSystem refused this application's machine token (attempt {Attempt})", attempt);
        }

        return (null, DefaultRetryAfter);
    }

    /// <summary>
    /// Whether <paramref name="applicationCode"/> names this application. Application codes are
    /// compared as the AuthSystem compares them: case-insensitively, in upper case.
    /// </summary>
    private bool IsThisApplication(string? applicationCode) =>
        !string.IsNullOrEmpty(applicationCode)
        && !string.IsNullOrEmpty(_options.ClientId)
        && string.Equals(applicationCode.ToUpperInvariant(), _options.ClientId.ToUpperInvariant(), StringComparison.Ordinal);

    private bool TryCached<TValue>(MemoryCache cache, string key, out TValue? value)
    {
        if (cache.TryGetValue(key, out CachedValue<TValue>? entry)
            && entry is not null
            && _time.GetUtcNow() < entry.ExpiresAt)
        {
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    private void Remember<TValue>(MemoryCache cache, string key, TValue value, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        // Size 1 each, so SizeLimit is a count of entries. A full cache drops the new entry and
        // compacts; the next request asks again.
        cache.Set(
            key,
            new CachedValue<TValue>(value, _time.GetUtcNow() + duration),
            new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = duration });
    }

    private static TimeSpan RetryAfterOf(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta
        ?? (response.Headers.RetryAfter?.Date is { } date && date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
        ?? DefaultRetryAfter;

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
    /// Resolves the named client, already carrying the base address and the
    /// gateway token from its registration.
    /// </summary>
    /// <remarks>
    /// This method used to set both again. IHttpClientFactory runs the
    /// registration delegate for EVERY CreateClient call, so the second
    /// DefaultRequestHeaders.Add appended rather than replaced: X-Gateway-Token
    /// is a custom header with no parser, so it accepts multiple values and went
    /// on the wire as "token, token". The API compares the header as a single
    /// string, so the length check failed before the comparison and every SDK
    /// call through a token-validating API came back 403.
    ///
    /// It survived because Gateway:ValidationEnabled is false in Development and
    /// true in Production, so the defect existed only where nobody was running
    /// it, and the SDK had no tests at all.
    /// </remarks>
    private HttpClient CreateClient() =>
        _httpClientFactory.CreateClient(AuthSystemConstants.HttpClientName);

    /// <summary>
    /// Computes a SHA256 hash of the raw key for use as a cache key.
    /// Never stores the raw key in cache.
    /// </summary>
    private static string ComputeCacheKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>A cached value and the instant it stops counting, read from <see cref="TimeProvider"/>.</summary>
    private sealed record CachedValue<TValue>(TValue Value, DateTimeOffset ExpiresAt);
}
