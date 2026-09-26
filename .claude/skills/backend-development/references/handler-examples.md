# Handler, Endpoint, Authorization, Caching and Logging Examples

Worked examples for `SKILL.md` → API Design (Endpoints, Authorization), Async and Cancellation, Caching and Logging. The rules are stated there and in the skills each comment names; this file only shows them applied. The error mapper `ToProblem()` and the validation behavior are in `error-contract-pipeline.md`.

## Endpoints

An endpoint sends one request through `ISender` and maps the result with the single mapper. No business logic, no status logic.

```csharp
// Minimal API. A body-bound command: validator property paths become errors[].pointer where errors is emitted.
app.MapPost("/api/v1/users", async (CreateUserCommand command, ISender sender, CancellationToken ct) =>
        (await sender.Send(command, ct)).Match<IResult>(
            created => TypedResults.Created($"/api/v1/users/{created.Id}", created),
            errors => errors.ToProblem()))
    .RequireAuthorization(UserPolicies.Manage); // permission check: endpoint metadata (Authorization, below)

// Controller: MVC executes an IResult returned by an action. A query bound from the query string has no
// body, so its validation failures carry no pointer (ProblemMapping.NoBody).
[HttpGet]
[ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
public async Task<IResult> GetUsers([FromQuery] GetUsersQuery query, CancellationToken cancellationToken) =>
    (await _sender.Send(query, cancellationToken)).Match<IResult>(
        page => TypedResults.Ok(page),
        errors => errors.ToProblem(ProblemMapping.NoBody));
```

## Authorization

The current-user port, a permission check as an endpoint policy, and a resource-level check in the handler.

```csharp
// Application port (layout (a): Application/Interfaces/; layout (b): MyApp.BuildingBlocks.Application).
public interface ICurrentUser
{
    // The authenticated caller's id; null when no account is authenticated (an anonymous endpoint, background work).
    Guid? Id { get; }
}

// Application, next to the port, registered as scoped. The code that creates a DI scope for a notification handler,
// a dispatcher or a worker sets it to the event's TriggeredBy before it resolves any service from that scope
// (/event-driven-architecture §4). It is never read from the request.
public sealed class CurrentUserOverride
{
    public bool IsSet { get; private set; }
    public Guid? Id { get; private set; }

    public void Set(Guid? triggeredBy)
    {
        if (IsSet) throw new InvalidOperationException("The scope's current user is already set.");
        (IsSet, Id) = (true, triggeredBy);
    }
}

// Api (layout (b): MyApp.BuildingBlocks.Api), registered as scoped by AddApi (layout (b): AddBuildingBlocks):
//   services.AddScoped<CurrentUserOverride>();
//   services.AddScoped<ICurrentUser, HttpCurrentUser>();
// A scope's explicit value wins; only without one does it read the user-id claim that the authentication scheme
// issues (ClaimTypes.NameIdentifier here). Under inline dispatch HttpContext still flows with the async call chain,
// so without the override a handler would stamp the request's user inline and null under the outbox dispatcher.
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, CurrentUserOverride scopeUser) : ICurrentUser
{
    public Guid? Id => scopeUser.IsSet
        ? scopeUser.Id
        : Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

// The scope a handler, dispatcher or worker creates:
//   await using var scope = scopeFactory.CreateAsyncScope();
//   scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().Set(notification.TriggeredBy);
//   var handler = scope.ServiceProvider.GetRequiredService<...>(); // audit stamping now reads TriggeredBy

// A handler behind an endpoint that requires an authenticated caller turns a missing id into a programming error
// (a BCL exception, never a catalog error) when the aggregate needs a non-null actor:
//   var actorId = currentUser.Id ?? throw new InvalidOperationException("This use case requires an authenticated caller.");

// Permission checks: policies registered by the module's Add{Module}Module (layout (a): the Api project's
// AddApi extension) and attached as endpoint metadata. A denial is the framework 403 with its transport code.
public static class OrderPolicies
{
    public const string Read = "orders:read";
}

services.AddAuthorizationBuilder()
    .AddPolicy(OrderPolicies.Read, policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "orders.read"));

app.MapGet("/api/v1/orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        (await sender.Send(new GetOrderByIdQuery(id), ct)).Match<IResult>(
            order => TypedResults.Ok(order),
            errors => errors.ToProblem(ProblemMapping.NoBody)))
    .RequireAuthorization(OrderPolicies.Read);

// Resource-level check: in the handler, after the resource is loaded.
public sealed class GetOrderByIdQueryHandler(IOrderRepository orders, ICurrentUser currentUser)
    : IRequestHandler<GetOrderByIdQuery, ErrorOr<OrderDto>>
{
    public async Task<ErrorOr<OrderDto>> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(query.OrderId, cancellationToken);

        // Only the order's own user may read it. Another user's order is reported as not found, so its existence is
        // not disclosed; where existence may be disclosed, the handler returns a catalog Forbidden error instead.
        if (order is null || order.UserId != currentUser.Id)
            return OrderErrors.NotFound(query.OrderId);

        return order.ToDto(); // one aggregate, loaded through its repository only to map it to a DTO
    }
}
```

## Command Handler

Placement option B: the command handler publishes after the commit (`/domain-driven-design` §4). Under option A the aggregate raises the event and the handler only persists.

This is an authenticated administrative creation. Public self-registration must not reveal an existing account: `/security-mindset` → Authentication.

```csharp
public sealed class CreateUserCommandHandler(
    IUserRepository users, IUnitOfWork unitOfWork, IPublisher publisher, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<CreateUserCommand, ErrorOr<UserDto>>
{
    public async Task<ErrorOr<UserDto>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        // Input validation already ran in the validation pipeline behavior (one FluentValidation validator per request).

        // A unique index backs this check under concurrency; a request that loses the race gets the index violation
        // as an unhandled DbException (500). A repository that must return 409 in that race records the duplicate-key
        // translation to UserErrors.DuplicateEmail in its ADR.
        if (await users.ExistsByEmailAsync(command.Email, cancellationToken))
            return UserErrors.DuplicateEmail; // a business outcome is returned, never thrown (P7)

        var created = User.Create(command.Email, command.Name); // the entity factory enforces its invariants
        if (created.IsError)
            return created.Errors;

        var user = created.Value;
        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken); // the commit takes the request token

        // Event contract: /event-driven-architecture §2. Value objects are unwrapped (user.Email.Value).
        await publisher.Publish(new UserCreatedEvent(
            EventId: Guid.NewGuid(),
            UserId: user.Id,
            Email: user.Email.Value,
            TriggeredBy: currentUser.Id,          // Guid?, like ICurrentUser.Id
            OccurredAt: timeProvider.GetUtcNow()),
            CancellationToken.None); // post-commit: not tied to the request (/event-driven-architecture §3)

        return user.ToDto(); // mapping goes entity -> DTO only
    }
}
```

## Notification Handler

Handler rules: `/event-driven-architecture` §4 (naming, scope, isolation, idempotency, critical or best-effort).

```csharp
/// <summary>
/// Sends the welcome email for a new user. Best-effort: a failure is logged and the email is dropped.
/// Idempotent on its natural key (one welcome email per user). <see cref="UserCreatedEvent.EventId"/> is passed as
/// the provider's idempotency key, so a repeat after a crash between the send and its record is deduplicated by
/// the provider.
/// </summary>
public sealed class WelcomeEmailEventHandler(
    IServiceScopeFactory scopes,
    ILogger<WelcomeEmailEventHandler> logger)
    : INotificationHandler<UserCreatedEvent>
{
    public async Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // This handler writes state (the sent-email record), so it uses its own DI scope and unit of work:
            // the command's unit of work has already committed.
            await using var scope = scopes.CreateAsyncScope();
            var sentEmails = scope.ServiceProvider.GetRequiredService<ISentEmailLog>();       // Application port
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();       // Application port
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            if (await sentEmails.WasSentAsync(notification.UserId, EmailKind.Welcome, cancellationToken))
                return;

            await emailSender.SendWelcomeAsync(
                notification.Email, idempotencyKey: notification.EventId, cancellationToken);
            await sentEmails.RecordAsync(notification.UserId, EmailKind.Welcome, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The cancellation filter (/failure-mode-design → Exception-Handling Boundaries): only this handler's own
            // token counts as cancellation, so a timeout's TaskCanceledException is a failure and is caught here.
            // Logged once; never rethrown into the publisher. Identifiers only, never the address.
            logger.LogError(ex, "Welcome email failed for event {EventId}, user {UserId}",
                notification.EventId, notification.UserId);
        }
    }
}
```

## Query Handler with Cache-Aside

Caches hold DTOs, never entities: an entity rebuilt by a serializer skips its factory and every invariant (`/domain-driven-design` §1).

```csharp
public sealed class GetUserByIdQueryHandler(IUserRepository users, ICacheService cache, IOptions<CacheOptions> options)
    : IRequestHandler<GetUserByIdQuery, ErrorOr<UserDto>>
{
    public async Task<ErrorOr<UserDto>> Handle(GetUserByIdQuery query, CancellationToken cancellationToken)
    {
        // ICacheService applies /failure-mode-design → Failure Responses for the cache.
        var cacheKey = CacheKeys.User(query.Id);
        var cached = await cache.GetAsync<UserDto>(cacheKey, cancellationToken);
        if (cached is not null) return cached;

        var user = await users.GetByIdAsync(query.Id, cancellationToken);
        if (user is null) return UserErrors.NotFound(query.Id);

        var dto = user.ToDto();
        await cache.SetAsync(cacheKey, dto, options.Value.UserTtl, cancellationToken); // the TTL comes from configuration
        return dto;
    }
}

// One class per bounded context builds every cache key of that context (layout (b): one per module, in its
// Application project). Every key starts with the context's name, so modules that share a cache never collide.
public static class CacheKeys
{
    private const string Context = "users";

    public static string User(Guid id) => $"{Context}:user:{id}";
    // Keys never contain personal data. For an email lookup the caller passes an HMAC-SHA-256 of the normalized
    // address (/security-mindset → Cryptography Standards), keyed with a key from the secret store
    // (/security-mindset → Secrets and Credential Storage).
    public static string UserByEmailHash(string emailHmac) => $"{Context}:user:email:{emailHmac}";
    public static string UserRoles(Guid userId) => $"{Context}:user:{userId}:roles";
}
```

## Logging Pipeline Behavior

Request outcomes are logged once, here. A rejection is an expected outcome, logged at Warning with its code; no business exception is caught. The request object is never logged.

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IErrorOr
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var operation = typeof(TRequest).Name;
        // TraceId and SpanId come from the activity; never mint a GUID for correlation.
        using (logger.BeginScope(new Dictionary<string, object> { ["Operation"] = operation }))
        {
            logger.LogInformation("Handling {Operation}", operation);
            var response = await next(); // the pipeline carries cancellationToken to the handler
            if (response.IsError)
                logger.LogWarning("{Operation} rejected with {ErrorCode}", operation, response.Errors![0].Code);
            else
                logger.LogInformation("{Operation} completed", operation);
            return response;
        }
    }
}
```

## Log Lines and Levels

```csharp
// ❌ BAD: a password, and an email address, which is personal data
_logger.LogInformation("User login: {Email}, Password: {Password}", email, password);

// ✅ GOOD: identifiers, not personal data
_logger.LogInformation("Login attempt for user {UserId}", userId);

// ✅ GOOD: before the user is known, a keyed hash (HMAC-SHA-256 with a key from the secret store,
// /security-mindset → Secrets and Credential Storage), never the address
_logger.LogInformation("Login attempt for {EmailHash}", emailHmac);
```

| Level | Method | Typical use |
|---|---|---|
| Trace | `LogTrace` | Detailed debugging: method entry and exit, variable values |
| Debug | `LogDebug` | Diagnostics: cache hits and misses |
| Information | `LogInformation` | Normal operation: a request handled, an order placed |
| Warning | `LogWarning` | Expected rejections, retries, fallbacks |
| Error | `LogError` | Failures that need attention |
| Critical | `LogCritical` | The process cannot continue, or a loss that must raise an alert; Serilog's Fatal |
