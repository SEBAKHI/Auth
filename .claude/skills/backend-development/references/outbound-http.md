# Outbound HTTP Client

Code for `SKILL.md` → Outbound HTTP and Async and Cancellation. The rules are stated there; the retry, timeout and breaker policy is `/failure-mode-design` → Retries, Timeouts and Circuit Breakers. All types here are Infrastructure, except the port the typed client implements (Application). The example service is a neutral external payment provider.

## Registration

One typed client, one resilience handler, options from configuration:

```csharp
// One typed client, one resilience handler. Policy: /failure-mode-design → Retries, Timeouts and Circuit Breakers.
builder.Services.AddTransient<ExternalServiceCredentialsHandler>();

builder.Services
    .AddHttpClient<IExternalServicePort, ExternalServiceClient>((sp, client) =>
        client.BaseAddress = sp.GetRequiredService<IOptions<ExternalServiceOptions>>().Value.BaseAddress) // ends with '/'
    .AddHttpMessageHandler<ExternalServiceCredentialsHandler>()
    .AddStandardResilienceHandler()
    .Configure(builder.Configuration.GetSection("ExternalServices:ExternalService:Resilience")) // retry, breaker and timeout options
    .Configure((options, sp) =>
    {
        options.Retry.DisableForUnsafeHttpMethods(); // safe methods only
        var logger = sp.GetRequiredService<ILogger<ExternalServiceClient>>();
        options.CircuitBreaker.OnOpened = args =>
        {
            logger.LogWarning(args.Outcome.Exception, "Circuit opened for {BreakDuration}", args.BreakDuration);
            return default;
        };
        options.CircuitBreaker.OnClosed = _ =>
        {
            logger.LogInformation("Circuit closed");
            return default;
        };
    });
```

A custom pipeline uses `AddResilienceHandler(name, (builder, context) => …)` with `HttpCircuitBreakerStrategyOptions` and `context.ServiceProvider`.

The options class binds the rest of the client's settings, and `HttpClient.Timeout` (the platform client timeout) is set in the same base-address lambda:

```csharp
builder.Services.AddOptions<ExternalServiceOptions>()
    .BindConfiguration("ExternalServices:ExternalService") // secret values come from a secret store behind configuration
    .ValidateDataAnnotations()
    .ValidateOnStart();

public sealed class ExternalServiceOptions
{
    [Required] public Uri BaseAddress { get; init; } = null!;   // ends with '/'
    [Required] public string ApiKey { get; init; } = null!;     // never a literal in source
    public TimeSpan ClientTimeout { get; init; }                // above the pipeline's total timeout
}
// In the AddHttpClient lambda: client.Timeout = options.ClientTimeout;
```

## Credentials

```csharp
// Registered on the client; callers never pass credentials, and logs never record the header.
public sealed class ExternalServiceCredentialsHandler(IOptionsMonitor<ExternalServiceOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-Api-Key");
        request.Headers.Add("X-Api-Key", options.CurrentValue.ApiKey);
        return base.SendAsync(request, cancellationToken);
    }
}

// OAuth 2.0 client credentials: a token-acquisition DelegatingHandler of the same shape obtains and caches the access
// token. It authenticates to the token endpoint with HTTP Basic (RFC 6749 §2.3.1), with the client id and secret from
// options bound to the secret store; never a hand-built form body that carries the secret, never literals.
```

## TLS Trust

Validation is never disabled. A private CA is trusted on its own client only:

```csharp
public static class TlsTrust
{
    public static IHttpClientBuilder TrustPrivateRoot(this IHttpClientBuilder builder, X509Certificate2 rootCa) =>
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            SslOptions =
            {
                CertificateChainPolicy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    CustomTrustStore = { rootCa },
                },
            },
        });

    // A development certificate: this registration fails startup in any environment other than Development.
    public static IHttpClientBuilder TrustDevelopmentRoot(
        this IHttpClientBuilder builder, IHostEnvironment environment, X509Certificate2 developmentCa) =>
        environment.IsDevelopment()
            ? builder.TrustPrivateRoot(developmentCa)
            : throw new InvalidOperationException("A development certificate may be trusted only in Development.");
}

// Usage (.NET 9+ loader): .TrustPrivateRoot(X509CertificateLoader.LoadCertificateFromFile(options.RootCaPath))
```

## The Shared Client Layer

```csharp
public interface IApiClient
{
    Task<ApiClientResponse<T>> GetAsync<T>(string uri, ApiRequestOptions? options = null, CancellationToken ct = default);
    Task<ApiClientResponse<TResponse>> PostAsync<TRequest, TResponse>(string uri, TRequest body, ApiRequestOptions? options = null, CancellationToken ct = default);
    Task<ApiClientResponse<TResponse>> PutAsync<TRequest, TResponse>(string uri, TRequest body, ApiRequestOptions? options = null, CancellationToken ct = default);
    Task<ApiClientResponse<TResponse>> PatchAsync<TRequest, TResponse>(string uri, TRequest body, ApiRequestOptions? options = null, CancellationToken ct = default);
    Task<ApiClientResponse<T>> DeleteAsync<T>(string uri, ApiRequestOptions? options = null, CancellationToken ct = default);
    Task<ApiClientResponse<TResponse>> PostMultipartAsync<TResponse>(string uri, MultipartFormDataContent content, ApiRequestOptions? options = null, CancellationToken ct = default);
}

public interface IApiClientFactory
{
    IApiClient Create(HttpClient httpClient); // the typed client's own HttpClient, with its handlers and resilience pipeline
}

public sealed record ApiRequestOptions
{
    public string MediaType { get; init; } = MediaTypeNames.Application.Json;           // key of the body serializer strategy
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(); // never credentials
    public IReadOnlyDictionary<string, string> QueryParams { get; init; } = new Dictionary<string, string>();
}

// One per response received. RawContent is read only by the anti-corruption table: never logged, never forwarded.
public sealed record ApiClientResponse<T>(
    bool IsSuccess,
    HttpStatusCode StatusCode,
    T? Data,
    string? RawContent,
    IReadOnlyDictionary<string, IEnumerable<string>> Headers,
    TimeSpan Elapsed);

// Body formats are strategies (P8): one serializer per media type, registered in DI.
// JsonBodySerializer, FormUrlEncodedBodySerializer, XmlBodySerializer and PlainTextBodySerializer implement it.
public interface IRequestBodySerializer
{
    string MediaType { get; }
    HttpContent Serialize<T>(T body);
}

public sealed class ApiClientFactory(IEnumerable<IRequestBodySerializer> serializers, ILoggerFactory loggers) : IApiClientFactory
{
    // Chosen by lookup on the media type, never by switch. Built once (singleton).
    private readonly FrozenDictionary<string, IRequestBodySerializer> _serializers =
        serializers.ToFrozenDictionary(s => s.MediaType, StringComparer.OrdinalIgnoreCase);

    public IApiClient Create(HttpClient httpClient) =>
        new ApiClient(httpClient, _serializers, loggers.CreateLogger<ApiClient>());
}

// Registered once for the host (layout (b): by AddBuildingBlocks). TryAddEnumerable skips a serializer type that is
// already registered, so a second registration never produces a duplicate media-type key in the lookup above.
//   services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestBodySerializer, JsonBodySerializer>());
//   services.TryAddEnumerable(ServiceDescriptor.Singleton<IRequestBodySerializer, FormUrlEncodedBodySerializer>());
//   services.TryAddSingleton<IApiClientFactory, ApiClientFactory>();

internal sealed partial class ApiClient(
    HttpClient http, FrozenDictionary<string, IRequestBodySerializer> serializers, ILogger<ApiClient> logger) : IApiClient
{
    // JsonSerializerOptions.Web (.NET 9+) is a shared read-only instance. A static `new JsonSerializerOptions(...)`
    // would stay mutable until first use (static mutable state, P4); on .NET 8, build it in a static initializer
    // and call MakeReadOnly() before storing it.
    private static JsonSerializerOptions Json => JsonSerializerOptions.Web;

    public async Task<ApiClientResponse<TResponse>> PostAsync<TRequest, TResponse>(
        string uri, TRequest body, ApiRequestOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ApiRequestOptions();
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = serializers[options.MediaType].Serialize(body) };
        return await SendAsync<TResponse>(request, options, ct);
    }

    // Get, Put, Patch, Delete and PostMultipart build their request the same way and call SendAsync.

    private async Task<ApiClientResponse<T>> SendAsync<T>(HttpRequestMessage request, ApiRequestOptions options, CancellationToken ct)
    {
        foreach (var (name, value) in options.Headers) request.Headers.TryAddWithoutValidation(name, value);
        // (QueryParams are appended to the relative URI with QueryHelpers.AddQueryString before the request is built.)

        var started = Stopwatch.GetTimestamp();
        // No catch: transport exceptions, timeouts and an open circuit propagate (an outage).
        using var response = await http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var elapsed = Stopwatch.GetElapsedTime(started);

        LogResponse(logger, request.Method.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, elapsed);
        var data = response.IsSuccessStatusCode && raw.Length > 0 ? JsonSerializer.Deserialize<T>(raw, Json) : default;
        return new ApiClientResponse<T>(response.IsSuccessStatusCode, response.StatusCode, data, raw,
            response.Headers.ToDictionary(h => h.Key, h => h.Value), elapsed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Upstream {Method} {Path} returned {Status} in {Elapsed}")]
    private static partial void LogResponse(ILogger logger, string method, string? path, int status, TimeSpan elapsed);
}
```

## Anti-Corruption Table

The typed client implements the Application port and returns `ErrorOr<T>`. No HTTP status, raw body or header crosses into Application, and the port's types never mirror the upstream's schema: the client sends and reads the upstream's own wire records and maps them to and from the port's types.

```csharp
public sealed class ExternalServiceClient(HttpClient httpClient, IApiClientFactory apiClients) : IExternalServicePort
{
    private readonly IApiClient _api = apiClients.Create(httpClient);

    public async Task<ErrorOr<PaymentDto>> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        var body = new UpstreamCreatePaymentBody(request.Amount, request.Currency, Reference: request.PaymentId.ToString());
        var response = await _api.PostAsync<UpstreamCreatePaymentBody, UpstreamPayment>("payments", body, ct: ct); // relative, no leading '/'
        if (!response.IsSuccess) return CreatePaymentUpstreamErrorTable.Translate(response);

        var payment = response.Data // an empty success body is unreadable: an outage
            ?? throw new HttpRequestException("Payments create: empty upstream body", null, response.StatusCode);
        return new PaymentDto(request.PaymentId, ProviderPaymentId: payment.Id, AcceptedAt: payment.CreatedAt);
    }
}

// The upstream's documented request and response bodies. Infrastructure types, used only inside this client.
internal sealed record UpstreamCreatePaymentBody(decimal Amount, string Currency, string Reference);
internal sealed record UpstreamPayment(string Id, DateTimeOffset CreatedAt);

// One table per upstream operation: the same upstream code can mean different things on different operations.
internal static class CreatePaymentUpstreamErrorTable
{
    private static readonly FrozenDictionary<string, Error> Known = new Dictionary<string, Error>
    {
        ["card_declined"]      = PaymentErrors.CardDeclined,
        ["insufficient_funds"] = PaymentErrors.InsufficientFunds,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static IEnumerable<Error> Rows => Known.Values; // for the contract test

    private static readonly FrozenSet<HttpStatusCode> Transient = new[]
    {
        HttpStatusCode.RequestTimeout, HttpStatusCode.TooManyRequests,
    }.ToFrozenSet();

    public static Error Translate<T>(ApiClientResponse<T> response)
    {
        // A transient status left after the retries is an outage.
        if (Transient.Contains(response.StatusCode) || (int)response.StatusCode >= 500)
            throw new HttpRequestException(
                $"Payments create: transient upstream status {(int)response.StatusCode}", null, response.StatusCode);

        var code = ExternalServiceBody.ReadCode(response.RawContent); // the upstream's documented error code, or null
        if (code is not null && Known.TryGetValue(code, out var error))
            return error;

        // Unmapped (unknown code, another 4xx, unreadable body): an outage. Status and code in the message, never the body.
        throw new HttpRequestException(
            $"Payments create: unmapped upstream answer {(int)response.StatusCode} {code ?? "(no code)"}", null, response.StatusCode);
    }
}
```

Variant, when the client contract names the outage with a catalog code: the unmapped and transient branches return the custom unavailable error (a catalog member of that type, `/domain-driven-design` §5) instead of throwing, and log the upstream status and code once, at Warning. Transport exceptions still propagate to the outage classifier.

```csharp
logger.LogWarning("Payments create: upstream answered {Status} {UpstreamCode}", (int)response.StatusCode, code);
return PaymentErrors.ProviderUnavailable; // Error.Custom of the custom unavailable type -> 503 through the Status Map
```

## Shorter Deadline

A caller that needs a shorter deadline than the client's policy links a timeout token. When that token fires, it rethrows as `TimeoutException`, which the outage classifier maps to 503.

```csharp
public async Task<ErrorOr<QuoteDto>> GetQuoteAsync(Guid id, CancellationToken ct)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
    deadline.CancelAfter(_options.Value.QuoteDeadline); // from configuration
    try
    {
        return await _quotes.GetAsync(id, deadline.Token);
    }
    catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
    {
        throw new TimeoutException("The quote lookup exceeded its deadline."); // the request itself was not cancelled
    }
}
```
