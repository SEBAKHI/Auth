# Application Integration Guide with AuthSystem

This guide explains how an external .NET application can authenticate and authorize requests from three sources using the AuthSystem SDK.

| Auth Method | Scheme Name | Source | Use Case |
|-------------|-------------|--------|----------|
| **JWT Bearer** | `Bearer` | `Authorization: Bearer <token>` header | Users authenticated via AuthSystem |
| **API Key** | `ApiKey` | `X-Api-Key: <key>` header | System-to-system communication |
| **Webhook Key** | `WebhookKey` | `?whk=<key>` URL query parameter | Webhook endpoint callers |

---

## Step 1: Install the Auth.Sdk Package

Add a reference to the `Auth.Sdk` project or NuGet package:

```xml
<!-- YourApp.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Auth.Sdk\Auth.Sdk.csproj" />
  <!-- OR once published as NuGet: -->
  <!-- <PackageReference Include="Auth.Sdk" Version="1.0.0" /> -->
</ItemGroup>
```

---

## Step 2: Configure appsettings.json

```json
{
  "AuthSystem": {
    "BaseUrl": "https://auth.example.com",
    "Issuer": "auth-system",
    "Audience": "auth-api",
    "GatewayToken": "your-gateway-token-here"
  }
}
```

| Setting | Description |
|---------|-------------|
| `BaseUrl` | The URL where your AuthSystem is running |
| `Issuer` | Must match the JWT issuer configured in the AuthSystem |
| `Audience` | Must match the JWT audience configured in the AuthSystem |
| `GatewayToken` | Inter-service token from AuthSystem (`POST /api/v1/admin/secrets/generate/gateway-token`) |

---

## Step 3: Register Authentication in Program.cs

```csharp
using Auth.Sdk.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register all three auth schemes in one call
builder.Services.AddAuthSystemAuthentication(options =>
{
    var config = builder.Configuration.GetSection("AuthSystem");
    options.BaseUrl = config["BaseUrl"]!;
    options.Issuer = config["Issuer"]!;
    options.Audience = config["Audience"]!;
    options.GatewayToken = config["GatewayToken"]!;

    // Optional: tune cache durations
    options.ApiKeyCacheDuration = TimeSpan.FromSeconds(60);     // default: 60s
    options.WebhookKeyCacheDuration = TimeSpan.FromMinutes(5);  // default: 5min
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
```

### What `AddAuthSystemAuthentication` Registers

| Scheme | Handler | Validation Method |
|--------|---------|-------------------|
| `Bearer` (default) | Built-in `JwtBearerHandler` | Local JWKS validation via `/.well-known/jwks.json` -- zero network cost per request |
| `ApiKey` | Custom `ApiKeyAuthenticationHandler` | Remote call to `POST /api/v1/apikeys/validate`, result cached for 60s |
| `WebhookKey` | Custom `WebhookKeyAuthenticationHandler` | Remote call to `POST /api/v1/webhookkeys/validate`, result cached for 5min |

---

## Step 4: Protect Your Controllers

### 4A. User Authentication (JWT Bearer)

For endpoints where **users** (authenticated via AuthSystem) interact with your application.

The SDK maps JWT custom claims so that ASP.NET Core role-based authorization works out of the box:
- `"roles"` claim -> `[Authorize(Roles = "...")]`
- `"sub"` claim -> `User.Identity.Name`

```csharp
[ApiController]
[Route("api/articles")]
[Authorize]  // Uses Bearer scheme by default
public class ArticlesController : ControllerBase
{
    [HttpGet]
    public IActionResult GetArticles()
    {
        // Access user claims from the JWT
        var userId = User.FindFirst("sub")?.Value;  // also available as User.Identity.Name
        var email = User.FindFirst("email")?.Value;
        var roles = User.FindAll("roles").Select(c => c.Value).ToList();
        var permissions = User.FindAll("permissions").Select(c => c.Value).ToList();

        return Ok(new { userId, email, roles });
    }
}
```

**Flow:**

1. User logs in via AuthSystem (`POST /api/v1/auth/login`) and receives a JWT access token
2. User sends requests to your application with `Authorization: Bearer <jwt-token>` header
3. The SDK validates the JWT locally using AuthSystem's public keys (JWKS) -- no HTTP call needed
4. Claims (`sub`, `email`, `roles`, `permissions`) are available via `User.Claims`

#### Role-Based Authorization

You can restrict endpoints to specific roles using `[Authorize(Roles = "...")]`:

```csharp
// Only users with the "Admin" role
[Authorize(Roles = "Admin")]
[HttpDelete("{id}")]
public IActionResult DeleteArticle(Guid id) { ... }

// Users with either "Admin" or "Editor" role
[Authorize(Roles = "Admin,Editor")]
[HttpPut("{id}")]
public IActionResult UpdateArticle(Guid id, [FromBody] UpdateRequest request) { ... }

// Combine multiple [Authorize] attributes for AND logic (must have both roles)
[Authorize(Roles = "Admin")]
[Authorize(Roles = "SuperUser")]
[HttpPost("dangerous-action")]
public IActionResult DangerousAction() { ... }

// Check roles programmatically
[Authorize]
[HttpGet("dashboard")]
public IActionResult Dashboard()
{
    if (User.IsInRole("Admin"))
    {
        // show admin dashboard
    }
    // ...
}
```

#### Permission-Based Authorization

For fine-grained control, use `[RequirePermission("...")]` from the SDK. This works with **both** JWT Bearer (`permissions` claim) and ApiKey (`scope`/`permission` claims), and supports wildcard matching.

```csharp
using Auth.Sdk.Authorization;

[ApiController]
[Route("api/articles")]
[Authorize]
public class ArticlesController : ControllerBase
{
    // Requires the "content:read" permission
    [RequirePermission("content:read")]
    [HttpGet]
    public IActionResult GetArticles() { ... }

    // Requires the "content:publish" permission
    [RequirePermission("content:publish")]
    [HttpPost("{id}/publish")]
    public IActionResult PublishArticle(Guid id) { ... }

    // Multiple permissions — user must have ALL (AND logic)
    [RequirePermission("content:write")]
    [RequirePermission("content:approve")]
    [HttpPost("{id}/approve-and-publish")]
    public IActionResult ApproveAndPublish(Guid id) { ... }
}
```

**Wildcard support:**

| User's Permission | Required Permission | Result |
|-------------------|-------------------|--------|
| `*` | _any_ | Granted |
| `content:*` | `content:read` | Granted |
| `content:*` | `content:publish` | Granted |
| `content:read` | `content:publish` | Denied |
| `app:content:*` | `app:content:read` | Granted |

**Cross-scheme compatibility:** The handler checks `permissions` (JWT), `permission` (ApiKey), and `scope` (ApiKey) claims, so `[RequirePermission]` works regardless of which authentication scheme was used.

---

### 4B. API Key Authentication (System-to-System)

For endpoints where **other systems** interact with your application using API keys.

```csharp
[ApiController]
[Route("api/content")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public class ContentApiController : ControllerBase
{
    [HttpGet("pages")]
    public IActionResult GetPages()
    {
        // Access API key claims
        var apiKeyId = User.FindFirst("apikey_id")?.Value;
        var applicationId = User.FindFirst("application_id")?.Value;
        var scopes = User.FindAll("scope").Select(c => c.Value).ToList();
        var environment = User.FindFirst("environment")?.Value;

        return Ok(new { apiKeyId, applicationId, scopes });
    }

    [HttpPost("publish")]
    public IActionResult PublishContent([FromBody] PublishRequest request)
    {
        // Check specific scope/permission
        var hasPublishScope = User.HasClaim("scope", "content:publish");
        if (!hasPublishScope)
            return Forbid();

        // ... publish logic
        return Ok();
    }
}
```

**Flow:**

1. Admin creates an API key in AuthSystem (`POST /api/v1/apikeys`) with specific permission scopes
2. The consuming system stores the API key (shown only once at creation)
3. The system sends requests to your application with `X-Api-Key: ak_prod_abc123...` header
4. The SDK calls AuthSystem to validate the key and caches the result for 60 seconds
5. Scopes/permissions from the API key are available as claims

---

### 4C. Webhook Key Authentication

For endpoints that **receive webhook calls** from other systems with a key in the URL.

```csharp
[ApiController]
[Route("api/webhooks")]
[Authorize(AuthenticationSchemes = "WebhookKey")]
public class WebhooksController : ControllerBase
{
    // Called as: POST /api/webhooks/content-updated?whk=wk_prod_xyz789...
    [HttpPost("content-updated")]
    public IActionResult OnContentUpdated([FromBody] WebhookPayload payload)
    {
        // Access webhook key claims
        var webhookKeyId = User.FindFirst("webhookkey_id")?.Value;
        var applicationId = User.FindFirst("application_id")?.Value;
        var targetUrl = User.FindFirst("target_url")?.Value;

        // ... process webhook
        return Ok();
    }
}
```

**Flow:**

1. Admin creates a webhook key in AuthSystem (`POST /api/v1/webhookkeys`) with a target URL
2. The calling system stores the webhook key (shown only once at creation)
3. The system calls your application's webhook URL with `?whk=wk_prod_xyz789...` in the query string
4. The SDK calls AuthSystem to validate the key and caches the result for 5 minutes
5. Webhook key metadata is available as claims

---

### 4D. Mixed Authentication

For endpoints that accept **multiple** auth methods:

```csharp
[ApiController]
[Route("api/data")]
[Authorize(AuthenticationSchemes = "Bearer,ApiKey")]  // accepts either JWT or API key
public class DataController : ControllerBase
{
    [HttpGet]
    public IActionResult GetData()
    {
        // Determine which scheme authenticated this request
        var scheme = User.Identity?.AuthenticationType;

        return scheme switch
        {
            "Bearer" => Ok(new { source = "user", userId = User.FindFirst("sub")?.Value }),
            "ApiKey" => Ok(new { source = "apikey", keyId = User.FindFirst("apikey_id")?.Value }),
            _ => Unauthorized()
        };
    }
}
```

---

## Step 5: Create Keys in AuthSystem

### Create an API Key

```http
POST /api/v1/apikeys
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "applicationId": "your-app-guid",
  "name": "Content Sync",
  "description": "Key for syncing content to your application",
  "environment": "production",
  "rateLimitPerMinute": 100,
  "rateLimitPerDay": 50000,
  "permissionIds": ["guid-of-content-read", "guid-of-content-publish"]
}
```

**Response** (key shown only once):

```json
{
  "id": "generated-guid",
  "apiKey": "ak_prod_aBcDeFgHiJkLmNoPqRsTuVwXyZ012345",
  "keyPrefix": "ak_prod_",
  "createdAt": "2026-03-22T...",
  "expiresAt": null
}
```

### Create a Webhook Key

```http
POST /api/v1/webhookkeys
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "applicationId": "your-app-guid",
  "name": "Content Update Webhook",
  "targetUrl": "https://app.example.com/api/webhooks/content-updated",
  "environment": "production"
}
```

**Response** (key shown only once):

```json
{
  "id": "generated-guid",
  "webhookKey": "wk_prod_aBcDeFgHiJkLmNoPqRsTuVwXyZ012345",
  "keyPrefix": "wk_prod_",
  "createdAt": "2026-03-22T...",
  "expiresAt": null
}
```

---

## Step 6: Request Examples

### User Request (JWT)

```http
GET /api/articles
Authorization: Bearer eyJhbGciOiJSUzI1NiIs...
```

### System Request (API Key)

```http
GET /api/content/pages
X-Api-Key: ak_prod_aBcDeFgHiJkLmNoPqRsTuVwXyZ012345
```

### Webhook Request (Webhook Key)

```http
POST /api/webhooks/content-updated?whk=wk_prod_aBcDeFgHiJkLmNoPqRsTuVwXyZ012345
Content-Type: application/json

{ "event": "content.updated", "data": { ... } }
```

---

## Claims Reference

| Claim | Bearer (JWT) | ApiKey | WebhookKey |
|-------|-------------|--------|------------|
| `sub` / `NameIdentifier` | User ID | API Key ID | Webhook Key ID |
| `email` | User email | -- | -- |
| `roles` | User roles | -- | -- |
| `permissions` | User permissions | -- | -- |
| `scope` / `permission` | -- | Key scopes | -- |
| `apikey_id` | -- | Key ID | -- |
| `application_id` | -- | App ID | App ID |
| `environment` | -- | Environment | Environment |
| `webhookkey_id` | -- | -- | Key ID |
| `target_url` | -- | -- | Target URL |

---

## Key Management Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/v1/apikeys` | `POST` | Create API key (requires `apikeys:create` permission) |
| `/api/v1/apikeys` | `GET` | List API keys by application |
| `/api/v1/apikeys/{id}/revoke` | `POST` | Revoke an API key |
| `/api/v1/apikeys/{id}/rotate` | `POST` | Rotate an API key with grace period |
| `/api/v1/apikeys/validate` | `POST` | Validate an API key (service-level auth) |
| `/api/v1/webhookkeys` | `POST` | Create webhook key (requires `webhookkeys:create` permission) |
| `/api/v1/webhookkeys` | `GET` | List webhook keys by application |
| `/api/v1/webhookkeys/{id}/revoke` | `POST` | Revoke a webhook key |
| `/api/v1/webhookkeys/{id}/rotate` | `POST` | Rotate a webhook key with grace period |
| `/api/v1/webhookkeys/validate` | `POST` | Validate a webhook key (service-level auth) |

---

## Security Notes

- **JWT tokens** are validated locally using cached JWKS -- no network call per request
- **API keys** use Argon2id hashing in the AuthSystem -- the raw key is shown only once at creation
- **Webhook keys** use HMAC-SHA256 hashing -- deterministic and fast for direct database lookup
- The SDK **never caches raw keys** -- it caches validation results keyed by a SHA256 hash of the raw key
- Webhook keys transmitted over non-HTTPS connections will trigger a warning log
- API key and webhook key validation endpoints require service-level authentication (gateway token)
