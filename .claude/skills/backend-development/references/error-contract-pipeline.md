# Error Contract Pipeline

Code for `SKILL.md` → Response Format. Every rule is stated there; this file shows one way to implement all of them in ASP.NET Core (.NET 8+ unless a line says otherwise). Placement: the API layer of layout (a), or `MyApp.BuildingBlocks.Api` in layout (b), except the validation behavior (Application; layout (b): `MyApp.BuildingBlocks.Application`).

## Bodies on the Wire

A handler error (409):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "instance": "/api/v1/reservations",
  "code": "Reservation.SlotTaken",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

A handler Validation result with two failures (400), in an API whose ADR publishes field-level validation. The second failure is on a query parameter, so it has no `pointer`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "instance": "/api/v1/orders",
  "code": "Order.QuantityOutOfRange",
  "errors": [
    { "code": "Order.QuantityOutOfRange", "pointer": "#/items/0/quantity" },
    { "code": "Order.ChannelUnknown" }
  ],
  "traceId": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
}
```

A Validation result with one failure, and every Validation result in an API that does not publish field-level validation, has the same body without `errors`.

`type` and `title` above are the framework defaults at the time of writing. Tests capture the real values from a run.

## Validation Behavior

```csharp
// Application: FluentValidation failures become ErrorOr Validation errors. Never throws.
public sealed class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest>? validator = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IErrorOr
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (validator is null) return await next();

        var result = await validator.ValidateAsync(request, cancellationToken);
        if (result.IsValid) return await next();

        var errors = result.Errors // rule-declaration order; the first failure is the primary error
            .ConvertAll(failure => Error.Validation(
                code: failure.ErrorCode,             // .WithErrorCode(...) on every rule: a catalog code
                description: failure.ErrorMessage,   // for developers and logs; never on the wire
                metadata: new Dictionary<string, object> { ["property"] = failure.PropertyName })); // "Items[0].Quantity"

        return (dynamic)errors; // ErrorOr<T> converts implicitly from List<Error>
    }
}

// A rule, with its catalog code (/domain-driven-design §5):
RuleFor(x => x.Email).NotEmpty().WithErrorCode(UserErrors.EmailRequired.Code);
```

## Status Map

```csharp
// API layer: the only ErrorType-to-status map in the solution.
public static class ErrorStatusMap
{
    private static readonly FrozenDictionary<int, int> Statuses = new Dictionary<int, int>
    {
        [(int)ErrorType.Validation]   = StatusCodes.Status400BadRequest,
        [(int)ErrorType.Unauthorized] = StatusCodes.Status401Unauthorized,
        [(int)ErrorType.Forbidden]    = StatusCodes.Status403Forbidden,
        [(int)ErrorType.NotFound]     = StatusCodes.Status404NotFound,
        [(int)ErrorType.Conflict]     = StatusCodes.Status409Conflict,
        [(int)ErrorType.Failure]      = StatusCodes.Status500InternalServerError,
        [(int)ErrorType.Unexpected]   = StatusCodes.Status500InternalServerError,
        // Only when the repository ADR declares the custom unavailable type (its constant: /domain-driven-design §5):
        [CustomErrorTypes.Unavailable] = StatusCodes.Status503ServiceUnavailable,
        // Each other Error.Custom type in the ADR, with the status the ADR records.
    }.ToFrozenDictionary();

    public static IReadOnlyCollection<int> DeclaredTypes => Statuses.Keys; // read by the coverage unit test

    public static int ToStatusCode(Error error) =>
        Statuses.GetValueOrDefault(error.NumericType, StatusCodes.Status500InternalServerError);
}
```

## Transport Codes

```csharp
// API layer: codes for errors the framework produces, in the default spelling. A client contract that prescribes
// codes prescribes these strings instead.
public static class TransportErrorCodes
{
    public const string BadRequest           = "Http.BadRequest";
    public const string Unauthenticated      = "Http.Unauthenticated";
    public const string Forbidden            = "Http.Forbidden";
    public const string NotFound             = "Http.NotFound";
    public const string MethodNotAllowed     = "Http.MethodNotAllowed";
    public const string ContentTooLarge      = "Http.ContentTooLarge";
    public const string UnsupportedMediaType = "Http.UnsupportedMediaType";
    public const string RateLimited          = "Http.RateLimited";
    public const string Unavailable          = "Http.Unavailable";
    public const string Unexpected           = "Http.Unexpected";

    private static readonly FrozenDictionary<int, string> ByStatus = new Dictionary<int, string>
    {
        [400] = BadRequest,       [401] = Unauthenticated,  [403] = Forbidden,
        [404] = NotFound,         [405] = MethodNotAllowed, [413] = ContentTooLarge,
        [415] = UnsupportedMediaType, [429] = RateLimited,
        [500] = Unexpected,       [502] = Unavailable,      [503] = Unavailable, [504] = Unavailable,
    }.ToFrozenDictionary();

    public static IReadOnlyCollection<string> All => ByStatus.Values.Distinct().ToArray(); // for the contract test

    // Any other framework-produced 4xx takes the 400 code; any other 5xx takes the 500 code.
    public static string For(int? status) =>
        status is int value && ByStatus.TryGetValue(value, out var code) ? code
        : status is >= 400 and < 500 ? BadRequest
        : Unexpected;
}

// Reason codes a challenge may record in place of the default transport code. Declared once, next to the transport
// codes, and published like them.
public static class ChallengeReasonCodes
{
    public const string TokenExpired = "Http.TokenExpired";

    public static readonly FrozenSet<string> All = new[] { TokenExpired }.ToFrozenSet(StringComparer.Ordinal); // for the contract test
}

// Lets a challenge record a reason for a body the framework will write (see Optional Challenge Reason).
public static class ProblemItems
{
    public const string Code = "problem.code";
}
```

## The Single Mapper

```csharp
public sealed record ProblemErrorEntry(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pointer);

public static class JsonPointer
{
    // The API's JSON naming policy (camelCase under the web defaults). Only the API layer knows it.
    private static readonly JsonNamingPolicy NamingPolicy = JsonNamingPolicy.CamelCase;

    // "Items[0].UnitPrice" -> "#/items/0/unitPrice": RFC 6901, URI-fragment form.
    public static string FromPropertyPath(string propertyPath) => "#/" + string.Join('/',
        propertyPath.Split(new[] { '.', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Uri.EscapeDataString(
                NamingPolicy.ConvertName(segment).Replace("~", "~0").Replace("/", "~1"))));
}

public static class ProblemMapping
{
    // Which validator property paths address the request body. A route or query value gets no pointer.
    public static readonly Func<string, bool> AllBody = static _ => true;   // a command bound from the body
    public static readonly Func<string, bool> NoBody = static _ => false;   // a query bound from the query string

    // The codes ErrorOr assigns when a factory call names none. Never declared, never emitted.
    public static readonly FrozenSet<string> ErrorOrDefaultCodes = new[]
    {
        "General.Failure", "General.Unexpected", "General.Validation", "General.Conflict",
        "General.NotFound", "General.Unauthorized", "General.Forbidden",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IResult ToProblem(this IReadOnlyList<Error> errors) => errors.ToProblem(AllBody);

    // A command with a route value passes, for example, `path => path != nameof(UpdateUserCommand.Id)`.
    public static IResult ToProblem(this IReadOnlyList<Error> errors, Func<string, bool> isBodyMember) =>
        errors.ToProblem(bodyPathFor: path => isBodyMember(path) ? path : null);

    // An endpoint whose request type reshapes the command's members maps each command property path to the body's
    // property path (null: not a body member), for example `path => path == "Email" ? "Contact.Email" : null`.
    public static IResult ToProblem(this IReadOnlyList<Error> errors, Func<string, string?> bodyPathFor)
    {
        foreach (var error in errors)
            if (ErrorOrDefaultCodes.Contains(error.Code))
                throw new InvalidOperationException( // a programming error: the response is the 500 problem, and it is logged
                    $"An error of type {error.Type} carries the ErrorOr default code {error.Code}; declare a catalog code.");

        var primary = errors[0]; // one ErrorType per result; the first error is the primary one
        var extensions = new Dictionary<string, object?> { ["code"] = primary.Code };

        // Only in an API whose ADR publishes field-level validation; otherwise delete this block.
        // It runs only for a Validation result with two or more failures: a single failure travels as `code` alone.
        if (primary.Type == ErrorType.Validation && errors.Count >= 2)
            extensions["errors"] = errors
                .Select(e => new ProblemErrorEntry(e.Code, PointerFor(e, bodyPathFor)))
                .ToArray();

        // type and title come from ProblemDetailsDefaults; traceId and instance are added by the writer.
        return TypedResults.Problem(statusCode: ErrorStatusMap.ToStatusCode(primary), extensions: extensions);
    }

    private static string? PointerFor(Error error, Func<string, string?> bodyPathFor) =>
        error.Metadata?.GetValueOrDefault("property") is string { Length: > 0 } property
            && bodyPathFor(property) is { Length: > 0 } bodyPath
            ? JsonPointer.FromPropertyPath(bodyPath)
            : null; // a model-level rule has an empty property path: no pointer, never "pointer": ""
}
```

## Wiring

```csharp
builder.Services.AddOptions<OutageOptions>()          // RetryAfterSeconds: the default Retry-After of a 503
    .BindConfiguration("ErrorContract:Outage")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    // Runs after ProblemDetailsDefaults and traceId, in every IProblemDetailsService write and in MVC's ProblemDetailsFactory.
    var http = context.HttpContext;
    var problem = context.ProblemDetails;

    problem.Instance ??= http.Request.Path;
    problem.Extensions.TryAdd("code",                        // handler problems already carry their code
        http.Items.TryGetValue(ProblemItems.Code, out var reason) && reason is string code
            ? code
            : TransportErrorCodes.For(problem.Status));

    if (problem.Status == StatusCodes.Status503ServiceUnavailable && !http.Response.Headers.ContainsKey(HeaderNames.RetryAfter))
        http.Response.Headers.RetryAfter = http.RequestServices.GetRequiredService<IOptions<OutageOptions>>()
            .Value.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

    // type and title are never set here.

    // Minimal APIs (.NET 10+ AddValidation, TypedResults.ValidationProblem) write an HttpValidationProblemDetails with a
    // dictionary-shaped `errors`: a second shape. Replace it with a plain problem; the writer serializes
    // context.ProblemDetails after this callback. MVC ignores replacement, so controllers use the factory below.
    if (problem is HttpValidationProblemDetails validation)
    {
        var plain = new ProblemDetails
        {
            Status = validation.Status, Title = validation.Title, Type = validation.Type, Instance = validation.Instance
        };
        foreach (var (key, value) in validation.Extensions)
            plain.Extensions[key] = value;                   // keeps traceId and code
        context.ProblemDetails = plain;
    }
});

builder.Services.AddExceptionHandler<ErrorContractExceptionHandler>();

// Limits and partitions: /security-mindset → Rate Limiting. The rejection mechanics:
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // the default is 503
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask; // no body of its own: UseStatusCodePages writes the problem
    };
});

// JSON options for minimal APIs and Microsoft.AspNetCore.OpenApi. Controllers set the same in AddJsonOptions.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;      // OpenAPI emits number, not [integer, string]
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());   // enums travel as names
    // RespectRequiredConstructorParameters and RespectNullableAnnotations (.NET 9+) stay off: requiredness is a validator rule.
});

// Controllers only: the automatic model-state 400 (malformed body, binding failure) gets the same shape, with the
// transport code and no errors. Requiredness is a FluentValidation rule, never an implicit [Required].
builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
{
    var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
    var problem = factory.CreateProblemDetails(context.HttpContext, StatusCodes.Status400BadRequest); // runs CustomizeProblemDetails
    return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
});

var app = builder.Build();
app.UseExceptionHandler();                    // an unhandled exception becomes a problem (500, or 503 for an outage)
app.UseStatusCodePages();                     // writes every EMPTY 4xx/5xx body through IProblemDetailsService
app.UseMiddleware<SheddingGateMiddleware>();  // after UseStatusCodePages, so its empty 503 gets the problem body
app.UseAuthentication();                      // after UseStatusCodePages, so empty 401/403 bodies are written
app.UseAuthorization();
app.UseRateLimiter();
// The developer exception page: Development only (WebApplication adds it only in Development; never add it elsewhere).
```

## Exception Handler and Outage Classifier

Boundaries: `/failure-mode-design` → Exception-Handling Boundaries.

```csharp
public sealed class ErrorContractExceptionHandler(
    IProblemDetailsService problems, ILogger<ErrorContractExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (http.Response.HasStarted) return false;
        // Aborted requests never reach this handler: for an OperationCanceledException or IOException with
        // RequestAborted cancelled, the middleware logs, sets 499 and returns before any IExceptionHandler runs.
        if (!OutageClassifier.IsOutage(exception)) return false; // the framework's 500 problem, with no detail

        // .NET 10+: the middleware no longer logs exceptions an IExceptionHandler handles, so log here.
        // .NET 8 and 9: the middleware has already logged it; delete this line.
        logger.LogError(exception, "Dependency outage");
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; // code Http.Unavailable, Retry-After added
        return await problems.TryWriteAsync(new() { HttpContext = http, Exception = exception });
    }
}

public static class OutageClassifier
{
    // The default list. HttpClient.Timeout throws TaskCanceledException with an inner TimeoutException. Polly's
    // ExecutionRejectedException covers the resilience handler's timeout and open circuit. Add only the exception types
    // that the repository ADR lists for its other dependencies (/failure-mode-design → Classify Every Dependency Use).
    public static bool IsOutage(Exception exception) =>
        exception is HttpRequestException or TimeoutException or Polly.ExecutionRejectedException
        || exception is TaskCanceledException { InnerException: TimeoutException };
}
```

The contract-named variant (a catalog code for an outage) is a handler result, not an exception: see `outbound-http.md` → Anti-Corruption Table.

## Shedding Gate

The one gate for deliberate shedding. Levels and the backlog threshold: `/failure-mode-design` → Graceful Degradation, Failure Responses.

```csharp
public interface ISheddingPolicy
{
    // True when the current degradation level disables this endpoint, or the outbox backlog throttles producers.
    bool ShouldShed(HttpContext context);
}

public sealed class SheddingGateMiddleware(RequestDelegate next, ISheddingPolicy policy, IOptionsMonitor<SheddingOptions> options)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (!policy.ShouldShed(context)) return next(context);

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter =
            options.CurrentValue.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture); // from configuration
        return Task.CompletedTask; // no body: UseStatusCodePages writes the problem with Http.Unavailable
    }
}
```

## Undeclared Query Keys

```csharp
// Rejects a query key the endpoint does not declare, before any handler runs: 400, transport code, no errors.
public sealed class RejectUndeclaredQueryKeys : IEndpointFilter
{
    private readonly FrozenSet<string> _declared;

    private RejectUndeclaredQueryKeys(IEnumerable<string> declared) =>
        _declared = declared.ToFrozenSet(StringComparer.OrdinalIgnoreCase); // binding ignores case too

    // The declared parameters are the public properties of the query the endpoint binds with [AsParameters].
    public static RejectUndeclaredQueryKeys For<TQuery>() =>
        new(typeof(TQuery).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name));

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Query.Keys.All(_declared.Contains)
            ? next(context)
            : ValueTask.FromResult<object?>(TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest)); // code added by CustomizeProblemDetails
}

app.MapGet("/api/v1/users", async ([AsParameters] GetUsersQuery query, ISender sender, CancellationToken ct) =>
        (await sender.Send(query, ct)).Match<IResult>(page => TypedResults.Ok(page), errors => errors.ToProblem(ProblemMapping.NoBody)))
    .AddEndpointFilter(RejectUndeclaredQueryKeys.For<GetUsersQuery>());
// Controllers: an IAsyncActionFilter makes the same comparison against the action's [FromQuery] parameters.
```

## Optional Challenge Reason

```csharp
// A reason code replaces the default transport code for this response. It comes from ChallengeReasonCodes, which
// the contract test checks for publication.
jwtOptions.Events = new JwtBearerEvents
{
    OnChallenge = context =>
    {
        if (context.AuthenticateFailure is SecurityTokenExpiredException)
            context.HttpContext.Items[ProblemItems.Code] = ChallengeReasonCodes.TokenExpired;
        return Task.CompletedTask; // WWW-Authenticate is kept; UseStatusCodePages writes the body
    }
};
```

## OpenAPI Schema Transformer

Microsoft.AspNetCore.OpenApi on .NET 9 (Microsoft.OpenApi 1.x). On .NET 10 the object model is Microsoft.OpenApi 2.x (`JsonSchemaType.String`, `IOpenApiSchema`); the three members are the same.

```csharp
builder.Services.AddOpenApi(options => options.AddSchemaTransformer((schema, context, _) =>
{
    if (context.JsonTypeInfo.Type != typeof(ProblemDetails)) return Task.CompletedTask;

    schema.Properties["code"] = new OpenApiSchema { Type = "string" };
    schema.Required.Add("code");
    schema.Properties["errors"] = new OpenApiSchema
    {
        Type = "array",
        Items = new OpenApiSchema
        {
            Type = "object",
            Properties = { ["code"] = new OpenApiSchema { Type = "string" }, ["pointer"] = new OpenApiSchema { Type = "string" } },
            Required = new HashSet<string> { "code" },
        },
    };
    schema.Properties["traceId"] = new OpenApiSchema { Type = "string" };
    return Task.CompletedTask;
}));
// Endpoints declare errors as: [ProducesResponseType(typeof(ProblemDetails), 400, "application/problem+json")]
// or .ProducesProblem(StatusCodes.Status400BadRequest). Never [Produces("application/json")] on error responses.
```

## OpenAPI Operation Transformer (List Queries)

`sortBy` binds as `string?` and `pageSize` as `int`, with no enum type and no `[Range]`, so a violation reaches the query's validator and gets its catalog code (SKILL.md → List Queries). The document still publishes the allowlist and the maximum, from the same constants the validator reads.

```csharp
// Application: the one source for both the validator and the document.
public static class UserListQuery
{
    public const int MaxPageSize = 100;
    public static readonly FrozenSet<string> SortableFields = FrozenSet.ToFrozenSet(["name", "createdAt"], StringComparer.Ordinal);
}
// The validator: RuleFor(q => q.PageSize).InclusiveBetween(1, UserListQuery.MaxPageSize).WithErrorCode(...);
//                RuleFor(q => q.SortBy).Must(s => s is null || UserListQuery.SortableFields.Contains(s)).WithErrorCode(...);

// Api: endpoints carry their allowlist and maximum as metadata; one transformer publishes them.
public sealed record ListQueryMetadata(IReadOnlySet<string> SortableFields, int MaxPageSize);

builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
{
    var list = context.Description.ActionDescriptor.EndpointMetadata.OfType<ListQueryMetadata>().SingleOrDefault();
    if (list is null || operation.Parameters is null) return Task.CompletedTask;

    foreach (var parameter in operation.Parameters)
    {
        if (parameter.Name == "sortBy")
            parameter.Schema.Enum = list.SortableFields.Select(f => (IOpenApiAny)new OpenApiString(f)).ToList();
        else if (parameter.Name == "pageSize")
            (parameter.Schema.Minimum, parameter.Schema.Maximum) = (1, list.MaxPageSize);
    }
    return Task.CompletedTask;
}));

// app.MapGet("/api/v1/users", ...)
//    .WithMetadata(new ListQueryMetadata(UserListQuery.SortableFields, UserListQuery.MaxPageSize));
```

Microsoft.OpenApi 1.x (.NET 9) as above; on .NET 10 (2.x) the enum values are `JsonNode`s and the bounds are strings.

## Contract Tests

Naming and integration-test rules: `/quality-assurance` → Test Naming, Integration Tests. One test per error path; these two are the validation paths. The endpoint requires `UserPolicies.Manage`, so they call it through the host's authenticated client (`/quality-assurance` → Integration Test Host, `CreateAuthenticatedClient`) with the scope that policy requires; the empty-401 path is tested with `CreateClient()`.

```csharp
public sealed class UsersErrorContractTests(ApiFactory factory) : IClassFixture<ApiFactory> // WebApplicationFactory<Program>
{
    // One failure: no errors, in every API.
    [Fact]
    public async Task CreateUser_WithoutEmail_Returns400WithEmailRequired()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await factory.CreateAuthenticatedClient("users.manage").PostAsJsonAsync("/api/v1/users", new { name = "New User" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync(ct);
        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("code").GetString().Should().Be(UserErrors.EmailRequired.Code);
        body.TryGetProperty("errors", out _).Should().BeFalse();
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.TryGetProperty("traceId", out _).Should().BeTrue();
        body.GetProperty("type").GetString().Should().Be(CapturedDefaults.Type400);   // captured from a real run
        body.GetProperty("title").GetString().Should().Be(CapturedDefaults.Title400);
        raw.Should().NotContain("Exception").And.NotContain("   at ");                // no exception data
    }

    // Two failures, in an API whose ADR publishes field-level validation (any other API asserts no errors here too).
    // The validator declares the Email rule before the Name rule.
    [Fact]
    public async Task CreateUser_WithoutEmailAndName_Returns400WithEmailRequired()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await factory.CreateAuthenticatedClient("users.manage").PostAsJsonAsync("/api/v1/users", new { }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
        body.GetProperty("code").GetString().Should().Be(UserErrors.EmailRequired.Code);  // equals errors[0].code
        var errors = body.GetProperty("errors");
        errors.GetArrayLength().Should().Be(2);
        errors[0].GetProperty("code").GetString().Should().Be(UserErrors.EmailRequired.Code);
        errors[0].GetProperty("pointer").GetString().Should().Be("#/email");
        errors[1].GetProperty("code").GetString().Should().Be(UserErrors.NameRequired.Code);
        errors[1].GetProperty("pointer").GetString().Should().Be("#/name");
        // The media type, status, traceId, type, title and no-exception-data assertions are the same as above.
    }
}
```

Build-failing tests. `PublishedErrorCodes` reads the code list the repository publishes to its clients.

```csharp
public sealed class ErrorCatalogContractTests
{
    // Every *Errors class in every Domain assembly (layout (b): every module's Domain, and MyApp.SharedKernel, which
    // holds the shared paging and sorting codes). A FrozenSet: a static array's elements could be reassigned (P4).
    private static readonly FrozenSet<Assembly> DomainAssemblies =
        new[] { typeof(UserErrors).Assembly /*, each module's Domain */ }.ToFrozenSet();

    private static IEnumerable<Error> CatalogErrors() =>
        DomainAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: true, IsSealed: true } && t.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(t =>
                t.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.FieldType == typeof(Error))
                    .Select(f => (Error)f.GetValue(null)!)
                .Concat(t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.ReturnType == typeof(Error))
                    .Select(m => (Error)m.Invoke(null, m.GetParameters()
                        .Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                        .ToArray())!)));

    private static readonly FrozenSet<string> ErrorOrDefaults = ProblemMapping.ErrorOrDefaultCodes;

    [Fact]
    public void CatalogCodes_ArePublished_AndNoneIsAnErrorOrDefault() =>
        CatalogErrors().Should().OnlyContain(e => PublishedErrorCodes.All.Contains(e.Code) && !ErrorOrDefaults.Contains(e.Code));

    [Fact]
    public void CatalogCodes_AreUniqueAcrossModules() => // a per-domain client contract groups by error domain first
        CatalogErrors().GroupBy(e => e.Code).Should().OnlyContain(g => g.Count() == 1);

    [Fact]
    public void TransportCodes_ArePublished_AndNoneIsAnErrorOrDefault() =>
        TransportErrorCodes.All.Concat(ChallengeReasonCodes.All)
            .Should().OnlyContain(c => PublishedErrorCodes.All.Contains(c) && !ErrorOrDefaults.Contains(c));

    [Fact]
    public void StatusMap_CoversEveryDeclaredErrorType() =>
        ErrorStatusMap.DeclaredTypes.Should().Contain(
            Enum.GetValues<ErrorType>().Select(t => (int)t).Append(CustomErrorTypes.Unavailable));
}

public sealed class ValidatorContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void EveryValidationRule_HasAPublishedErrorCode()
    {
        using var scope = factory.Services.CreateScope();
        var components = typeof(CreateUserCommandValidator).Assembly.GetTypes() // each Application assembly
            .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(IValidator)))
            .Select(t => (IValidator)ActivatorUtilities.CreateInstance(scope.ServiceProvider, t))
            .SelectMany(v => v.CreateDescriptor().Rules.SelectMany(r => r.Components));

        components.Should().OnlyContain(c => c.ErrorCode != null && PublishedErrorCodes.All.Contains(c.ErrorCode));
    }

    [Fact]
    public async Task OpenApiProblemDetailsSchema_HasCodeErrorsAndTraceId()
    {
        var document = JsonDocument.Parse(await factory.CreateClient()
            .GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ProblemDetails").GetProperty("properties");
        new[] { "code", "errors", "traceId" }.Should().OnlyContain(name => properties.TryGetProperty(name, out _));
    }
}

// Every anti-corruption table exposes its rows to the test assembly (InternalsVisibleTo):
//   CreatePaymentUpstreamErrorTable.Rows.Should().OnlyContain(e => PublishedErrorCodes.All.Contains(e.Code));
```
