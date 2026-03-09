---
name: backend-development
description: Load this skill when implementing backend APIs, services, repositories, or any server-side code. Covers clean architecture, SOLID principles, REST API design, unified response format, centralized HTTP client, async patterns, database best practices, caching, and logging standards.
user-invocable: true
---

# Backend Development

## Architecture Principles

### 1. Clean Architecture

> **For full layer structure, folder conventions, responsibilities, restrictions, and dependency graph, invoke `/clean-architecture-structure`.**
>
> Key rule: Dependencies flow inward. Domain depends on nothing. API → Application → Domain. Infrastructure/Persistence → Application/Domain.

### 2. SOLID Principles Application

```csharp
// ═══════════════════════════════════════════════════════════
// SINGLE RESPONSIBILITY PRINCIPLE
// Each class has one reason to change
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Multiple responsibilities
public class UserService
{
    public User CreateUser(UserDto dto) { /* ... */ }
    public void SendWelcomeEmail(User user) { /* ... */ }
    public string GenerateReport(List<User> users) { /* ... */ }
}

// ✅ GOOD: Separated responsibilities
public class UserService { /* User CRUD operations */ }
public class EmailService { /* Email operations */ }
public class ReportService { /* Report generation */ }

// ═══════════════════════════════════════════════════════════
// OPEN/CLOSED PRINCIPLE
// Open for extension, closed for modification
// ═══════════════════════════════════════════════════════════

// ✅ GOOD: Extensible through abstraction
public interface IPaymentProcessor
{
    Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct);
}

public class StripeProcessor : IPaymentProcessor { /* ... */ }
public class PayPalProcessor : IPaymentProcessor { /* ... */ }
public class CryptoProcessor : IPaymentProcessor { /* ... */ } // New, no changes needed

// ═══════════════════════════════════════════════════════════
// LISKOV SUBSTITUTION PRINCIPLE
// Subtypes must be substitutable for their base types
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Violates LSP
public class Rectangle
{
    public virtual int Width { get; set; }
    public virtual int Height { get; set; }
}

public class Square : Rectangle
{
    public override int Width
    {
        set { base.Width = base.Height = value; } // Unexpected behavior
    }
}

// ✅ GOOD: Proper abstraction
public interface IShape
{
    int Area { get; }
}

public class Rectangle : IShape { /* ... */ }
public class Square : IShape { /* ... */ }

// ═══════════════════════════════════════════════════════════
// INTERFACE SEGREGATION PRINCIPLE
// Clients should not depend on interfaces they don't use
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Fat interface
public interface IRepository<T>
{
    T GetById(int id);
    IEnumerable<T> GetAll();
    void Add(T entity);
    void Update(T entity);
    void Delete(T entity);
    void BulkInsert(IEnumerable<T> entities);
    IEnumerable<T> ExecuteQuery(string sql);
}

// ✅ GOOD: Segregated interfaces
public interface IReadRepository<T>
{
    T GetById(int id);
    IEnumerable<T> GetAll();
}

public interface IWriteRepository<T>
{
    void Add(T entity);
    void Update(T entity);
    void Delete(T entity);
}

// ═══════════════════════════════════════════════════════════
// DEPENDENCY INVERSION PRINCIPLE
// Depend on abstractions, not concretions
// ═══════════════════════════════════════════════════════════

// ❌ BAD: Direct dependency
public class OrderService
{
    private readonly SqlOrderRepository _repository = new SqlOrderRepository();
}

// ✅ GOOD: Dependency injection
public class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }
}
```

### 3. API Design Standards

```csharp
// ═══════════════════════════════════════════════════════════
// RESTful API Design
// ═══════════════════════════════════════════════════════════

// Resource naming: plural nouns, lowercase, hyphens for multi-word
// GET    /api/v1/users              → List users
// GET    /api/v1/users/{id}         → Get user by ID
// POST   /api/v1/users              → Create user
// PUT    /api/v1/users/{id}         → Update user (full)
// PATCH  /api/v1/users/{id}         → Update user (partial)
// DELETE /api/v1/users/{id}         → Delete user

// Nested resources for relationships
// GET    /api/v1/users/{id}/orders  → Get user's orders

// Query parameters for filtering, sorting, pagination
// GET    /api/v1/users?status=active&sort=-createdAt&page=1&limit=20

/// <summary>
/// Retrieves a paginated list of users with optional filtering.
/// </summary>
[HttpGet]
[ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
public async Task<ActionResult<PagedResult<UserDto>>> GetUsers(
    [FromQuery] UserFilter filter,
    CancellationToken cancellationToken)
{
    var result = await _userService.GetUsersAsync(filter, cancellationToken);
    return Ok(result);
}

// ═══════════════════════════════════════════════════════════
// Error Response Format (RFC 7807 Problem Details)
// ═══════════════════════════════════════════════════════════

{
  "type": "https://api.example.com/errors/validation",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/users",
  "traceId": "00-1234567890abcdef-fedcba0987654321-00",
  "errors": {
    "email": ["Email format is invalid"],
    "password": ["Password must be at least 8 characters"]
  }
}
```

### Unified Response Format (MANDATORY)

**All API endpoints MUST use a consistent response format:**

```csharp
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public ApiError? Error { get; set; }
    public PaginationMeta? Pagination { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> SuccessResult(T data, string message = "Success")
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> FailResult(string message, ApiError? error = null)
        => new() { Success = false, Message = message, Error = error };

    public static ApiResponse<T> PagedResult(T data, PaginationMeta pagination)
        => new() { Success = true, Data = data, Pagination = pagination };
}

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}

public class PaginationMeta
{
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public bool HasPrevious => CurrentPage > 1;
    public bool HasNext => CurrentPage < TotalPages;
}

// Usage in Controllers
[HttpGet("{id}")]
public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(Guid id, CancellationToken ct)
{
    var user = await _userService.GetByIdAsync(id, ct);
    if (user == null)
        return NotFound(ApiResponse<UserDto>.FailResult(
            "User not found",
            new ApiError { Code = "USER_NOT_FOUND", Detail = $"No user exists with ID {id}" }));
    return Ok(ApiResponse<UserDto>.SuccessResult(user));
}
```

**JSON Response Examples:**

```json
// Success Response
{
    "success": true,
    "data": { "id": "123e4567-...", "email": "user@example.com", "name": "John Doe" },
    "message": "Success",
    "error": null,
    "pagination": null,
    "traceId": "00-1234567890abcdef-fedcba0987654321-00",
    "timestamp": "2024-01-15T10:30:00Z"
}

// Error Response
{
    "success": false,
    "data": null,
    "message": "Validation failed",
    "error": {
        "code": "VALIDATION_ERROR",
        "detail": "One or more validation errors occurred.",
        "validationErrors": {
            "email": ["Email format is invalid"],
            "password": ["Password must be at least 8 characters"]
        }
    },
    "pagination": null,
    "traceId": "00-fedcba0987654321-1234567890abcdef-00",
    "timestamp": "2024-01-15T10:30:00Z"
}
```

### Centralized API Request Client (MANDATORY)

**All outgoing HTTP requests MUST use a centralized client class that provides:**
- Consistent configuration (timeouts, retries, headers)
- Body format selection (JSON, Form, Multipart)
- Authentication handling
- Logging and tracing
- Error handling

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

public class ApiRequestOptions
{
    public BodyFormat BodyFormat { get; set; } = BodyFormat.Json;
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, string> QueryParams { get; set; } = new();
    public TimeSpan? Timeout { get; set; }
    public int? RetryCount { get; set; }
    public AuthenticationScheme? AuthScheme { get; set; }
    public string? BearerToken { get; set; }
    public string? ApiKey { get; set; }
    public bool SkipSslValidation { get; set; } = false;
}

public enum BodyFormat { Json, FormUrlEncoded, Xml, PlainText }
public enum AuthenticationScheme { None, Bearer, ApiKey, Basic }

public class ApiClientResponse<T>
{
    public bool IsSuccess { get; set; }
    public HttpStatusCode StatusCode { get; set; }
    public T? Data { get; set; }
    public string? RawContent { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, IEnumerable<string>> Headers { get; set; } = new();
    public TimeSpan Elapsed { get; set; }
}

// Usage Examples

// JSON POST (default)
var result = await _apiClient.PostAsync<CreateUserRequest, UserDto>(
    "/api/users",
    new CreateUserRequest { Email = "test@example.com", Name = "Test" },
    cancellationToken: ct);

// Form URL encoded POST
var formResult = await _apiClient.PostAsync<Dictionary<string, string>, TokenResponse>(
    "/oauth/token",
    new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = "my-client",
        ["client_secret"] = "secret"
    },
    new ApiRequestOptions { BodyFormat = BodyFormat.FormUrlEncoded },
    ct);

// GET with API key auth
var apiKeyResult = await _apiClient.GetAsync<DataResponse>(
    "/external/data",
    new ApiRequestOptions { AuthScheme = AuthenticationScheme.ApiKey, ApiKey = "external-api-key-123" },
    ct);
```

### 4. Async/Await and CancellationToken

**EVERY async method MUST accept and propagate CancellationToken:**

```csharp
// ✅ GOOD: Full CancellationToken propagation
public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();

    var validationResult = await _validator.ValidateAsync(request, cancellationToken);
    if (!validationResult.IsValid)
        throw new ValidationException(validationResult.Errors);

    var existingUser = await _repository.GetByEmailAsync(request.Email, cancellationToken);
    if (existingUser != null)
        throw new DuplicateEmailException(request.Email);

    var user = new User(request.Email, request.Name);
    await _repository.AddAsync(user, cancellationToken);
    await _repository.SaveChangesAsync(cancellationToken);

    try
    {
        await _emailService.SendWelcomeEmailAsync(user, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogWarning(ex, "Failed to send welcome email to {Email}", user.Email);
    }

    return user.ToDto();
}

// ✅ GOOD: Repository with CancellationToken
public async Task<List<User>> GetByFilterAsync(UserFilter filter, CancellationToken cancellationToken)
{
    var query = _context.Users.AsQueryable();
    if (!string.IsNullOrEmpty(filter.Search))
        query = query.Where(u => u.Name.Contains(filter.Search));
    if (filter.Status.HasValue)
        query = query.Where(u => u.Status == filter.Status.Value);
    return await query.OrderBy(u => u.Name).Skip(filter.Skip).Take(filter.Take).ToListAsync(cancellationToken);
}
```

### 5. Database Best Practices

#### Database Normalization (MANDATORY)

**All database designs MUST adhere to all five normal forms (5NF):**

```
┌─────────────────────────────────────────────────────────────┐
│              DATABASE NORMALIZATION REQUIREMENTS            │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1NF - First Normal Form                                    │
│  □ Eliminate repeating groups                               │
│  □ Create separate table for related data                   │
│  □ Identify each set with a primary key                     │
│                                                             │
│  2NF - Second Normal Form                                   │
│  □ Meet all 1NF requirements                                │
│  □ Remove subsets of data to separate tables                │
│  □ Create relationships using foreign keys                  │
│                                                             │
│  3NF - Third Normal Form                                    │
│  □ Meet all 2NF requirements                                │
│  □ Remove columns not dependent on primary key              │
│  □ Eliminate transitive dependencies                        │
│                                                             │
│  4NF - Fourth Normal Form                                   │
│  □ Meet all 3NF requirements                                │
│  □ Remove multi-valued dependencies                         │
│  □ No table may contain two+ independent multi-valued facts │
│                                                             │
│  5NF - Fifth Normal Form (Project-Join Normal Form)         │
│  □ Meet all 4NF requirements                                │
│  □ Cannot be decomposed into smaller tables without loss    │
│  □ Every join dependency is implied by candidate keys       │
│                                                             │
│  EXCEPTIONS:                                                │
│  Denormalization is permitted ONLY when:                    │
│  • Documented performance requirements demand it            │
│  • The trade-off is explicitly approved                     │
│  • Data integrity is maintained via other means             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

#### Connection Management & Parameterized Queries

```csharp
// ✅ GOOD: Use connection pooling, short-lived connections
public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
{
    await using var connection = new SqlConnection(_connectionString);
    var command = new CommandDefinition(
        "SELECT * FROM Users WHERE Id = @Id AND IsDeleted = 0",
        new { Id = id },
        cancellationToken: cancellationToken);
    return await connection.QuerySingleOrDefaultAsync<User>(command);
}

// ❌ BAD: SQL Injection vulnerability
var sql = $"SELECT * FROM Users WHERE Email = '{email}'";

// ✅ GOOD: Parameterized query
var sql = "SELECT * FROM Users WHERE Email = @Email";
var user = await connection.QuerySingleOrDefaultAsync<User>(sql, new { Email = email });

// ✅ GOOD: Transaction management
public async Task TransferFundsAsync(Guid fromAccount, Guid toAccount, decimal amount, CancellationToken ct)
{
    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync(ct);
    await using var transaction = await connection.BeginTransactionAsync(ct);
    try
    {
        await connection.ExecuteAsync("UPDATE Accounts SET Balance = Balance - @Amount WHERE Id = @Id", new { Id = fromAccount, Amount = amount }, transaction);
        await connection.ExecuteAsync("UPDATE Accounts SET Balance = Balance + @Amount WHERE Id = @Id", new { Id = toAccount, Amount = amount }, transaction);
        await transaction.CommitAsync(ct);
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}
```

### 6. Caching Strategy

```csharp
// Cache-Aside Pattern
public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
{
    var cacheKey = $"user:{id}";
    var cachedUser = await _cache.GetAsync<User>(cacheKey, cancellationToken);
    if (cachedUser != null) return cachedUser;

    var user = await _innerRepository.GetByIdAsync(id, cancellationToken);
    if (user != null)
        await _cache.SetAsync(cacheKey, user, TimeSpan.FromMinutes(5), cancellationToken);
    return user;
}

// Cache Key Conventions
public static class CacheKeys
{
    public static string User(Guid id) => $"user:{id}";
    public static string UserByEmail(string email) => $"user:email:{email.ToLowerInvariant()}";
    public static string UserRoles(Guid userId) => $"user:{userId}:roles";
    public static string UserPermissions(Guid userId) => $"user:{userId}:permissions";
    public static string Config(string key) => $"config:{key}";
}
```

### 7. Logging Standards

```csharp
// ✅ GOOD: Structured logging with context
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["Email"] = request.Email,
    ["RequestId"] = Activity.Current?.Id ?? Guid.NewGuid().ToString()
}))
{
    _logger.LogInformation("Creating new user");
    try
    {
        var user = await _repository.CreateAsync(request, ct);
        _logger.LogInformation("User created successfully with ID {UserId}", user.Id);
        return user;
    }
    catch (DuplicateEmailException ex)
    {
        _logger.LogWarning(ex, "Failed to create user - email already exists");
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error creating user");
        throw;
    }
}

// Log Level Guidelines:
// TRACE   → Detailed debugging (method entry/exit, variable values)
// DEBUG   → Diagnostic information (cache hits/misses, query results)
// INFO    → Normal operation events (user created, order processed)
// WARNING → Unexpected but handled situations (retry, fallback used)
// ERROR   → Failures requiring attention (exception caught, operation failed)
// FATAL   → Critical failures (app startup failed, unrecoverable state)

// ❌ BAD: Logging sensitive data
_logger.LogInformation("User login: {Email}, Password: {Password}", email, password);

// ✅ GOOD: Redact sensitive data
_logger.LogInformation("User login attempt for {Email}", email);
```
