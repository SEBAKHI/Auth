# C# Test Examples

Read this when you write C# unit or integration tests, or configure coverage (coverlet) in a .NET solution.

This file holds examples only. Every rule they apply is stated in `SKILL.md`: Coverage Gate, Coverage Exclusions, Test Naming, Integration Tests, Test Stack (C#/.NET). The handlers under test are the canonical ones of `/backend-development`; the error catalog is `/domain-driven-design` §5 Domain Errors.

---

## Handler Unit Tests

`CreateUserCommandHandler` publishes after the commit (Placement option B, `/domain-driven-design` §4). The success test also covers the token rule of Test Stack (C#/.NET): every call up to and including the commit is verified with the test token, and the publish with `CancellationToken.None`.

No test builds an entity with a constructor. A test that needs an entity creates it through its factory: `User.Create(...).Value`.

```csharp
// C# (xUnit v3 + Moq + FluentAssertions 7.x). Placement option B: the command handler publishes after the commit.
public sealed class CreateUserCommandHandlerTests
{
    private const string Email = "new.user@example.com";
    private const string Name = "New User";

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly FakeTimeProvider _time = new(); // Microsoft.Extensions.TimeProvider.Testing
    private readonly CreateUserCommandHandler _sut;

    public CreateUserCommandHandlerTests() =>
        _sut = new CreateUserCommandHandler(_users.Object, _unitOfWork.Object, _publisher.Object, _currentUser.Object, _time);

    [Fact]
    public async Task Handle_WithNewEmail_ReturnsCreatedUser()
    {
        var ct = TestContext.Current.CancellationToken;
        _users.Setup(r => r.ExistsByEmailAsync(Email, ct)).ReturnsAsync(false);

        var result = await _sut.Handle(new CreateUserCommand(Email, Name), ct);

        result.IsError.Should().BeFalse();
        result.Value.Email.Should().Be(Email);
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), ct), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
        _publisher.Verify(p => p.Publish(It.IsAny<UserCreatedEvent>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_WithDuplicateEmail_ReturnsDuplicateEmailError()
    {
        var ct = TestContext.Current.CancellationToken;
        _users.Setup(r => r.ExistsByEmailAsync(Email, ct)).ReturnsAsync(true);

        var result = await _sut.Handle(new CreateUserCommand(Email, Name), ct);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(UserErrors.DuplicateEmail.Code);
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<UserCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

A query handler returns a missing resource as a catalog error, never as null. The handler under test is the cache-aside query handler of `/backend-development`: a loose Moq cache returns no hit, and `Options.Create` supplies the cache TTL that the handler reads from configuration.

```csharp
// C# (xUnit v3 + Moq + FluentAssertions 7.x).
public sealed class GetUserByIdQueryHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly IOptions<CacheOptions> _options = Options.Create(new CacheOptions { UserTtl = TimeSpan.FromMinutes(5) });
    private readonly GetUserByIdQueryHandler _sut;

    public GetUserByIdQueryHandlerTests() => _sut = new GetUserByIdQueryHandler(_users.Object, _cache.Object, _options);

    [Fact]
    public async Task Handle_WithNonExistentId_ReturnsNotFoundError()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _users.Setup(r => r.GetByIdAsync(id, ct)).ReturnsAsync((User?)null);

        var result = await _sut.Handle(new GetUserByIdQuery(id), ct);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(UserErrors.NotFound(id).Code);
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WithExistingId_ReturnsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = User.Create("existing.user@example.com", "Existing User").Value; // factory, never a constructor
        _users.Setup(r => r.GetByIdAsync(user.Id, ct)).ReturnsAsync(user);

        var result = await _sut.Handle(new GetUserByIdQuery(user.Id), ct);

        result.IsError.Should().BeFalse();
        result.Value.Email.Should().Be("existing.user@example.com");
    }
}
```

---

## Integration Test Host

Integration Tests rules 1–4 in one fixture: the production database engine in a container (PostgreSQL here; use the engine the solution runs on), `WebApplicationFactory<Program>`, and every outbound HTTP client pointed at a stub handler. Only the external adapters' transport is replaced; the rest of the composition root runs as in production, authentication and authorization policies included. `CreateAuthenticatedClient` signs a bearer token with a test key that the host trusts through configuration; `CreateClient()` stays the anonymous client for the empty-401 path.

```csharp
// C# (xUnit v3 + Testcontainers + Microsoft.AspNetCore.Mvc.Testing + System.IdentityModel.Tokens.Jwt).
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Layout (a): the single "Default" connection string. Layout (b): one entry per module, named as the host reads them.
    private static readonly string[] ConnectionStringNames = ["Default"];
    private const string Issuer = "https://tests.local";
    private const string Audience = "api";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder().Build();
    private readonly SymmetricSecurityKey _signingKey = new(RandomNumberGenerator.GetBytes(32));

    public StubHttpMessageHandler Upstream { get; } = new();

    // Scopes must satisfy the endpoint's policy, for example "users.manage" for UserPolicies.Manage.
    public HttpClient CreateAuthenticatedClient(params string[] scopes)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()), new Claim("scope", string.Join(' ', scopes))],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Rule 1: every connection string the host reads points at the container. Layout (a): ConnectionStrings:Default; layout (b): one per module.
        foreach (var name in ConnectionStringNames)
            builder.UseSetting($"ConnectionStrings:{name}", _database.GetConnectionString());

        // The signing key is configuration, not a replaced component (Integration Tests rule 2): the real JwtBearer validation runs.
        // Use the configuration keys the host's JwtBearer setup reads.
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(_signingKey.Key));

        builder.ConfigureTestServices(services =>
            // Rule 4: every IHttpClientFactory client (typed clients included) talks to the stub; CI never calls a live third party.
            services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => Upstream)));
    }
}

// Rule 4: fake at the HttpMessageHandler level. A call the test did not script fails the test.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response) => _responses.Enqueue(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _responses.TryDequeue(out var respond)
            ? Task.FromResult(respond(request))
            : throw new InvalidOperationException($"Unscripted outbound call: {request.Method} {request.RequestUri}");
}
```

Rule 3 (each test owns its data): reset the database before each test (for example with Respawn, from the test class's `InitializeAsync`), or run each test in a transaction that it rolls back.

---

## Endpoint Integration Test

Named `{Endpoint}_{Scenario}_Returns{Status}With{ErrorName}`. The full set of body assertions for each error path is `/backend-development` → Contract Tests; this example shows the shape of the test, not that list.

```csharp
// C# (xUnit v3 + FluentAssertions 7.x).
public sealed class CreateUserEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateUser_WithDuplicateEmail_Returns409WithDuplicateEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateAuthenticatedClient("users.manage"); // satisfies UserPolicies.Manage
        var request = new { email = "taken@example.com", name = "First User" };
        (await client.PostAsJsonAsync("/api/v1/users", request, ct)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/users", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        body.RootElement.GetProperty("code").GetString().Should().Be(UserErrors.DuplicateEmail.Code);
        // ...and the remaining assertions of /backend-development → Contract Tests.
    }

    [Fact]
    public async Task CreateUser_WithoutCredentials_Returns401WithUnauthenticated()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient(); // anonymous: the empty-401 path

        var response = await client.PostAsJsonAsync("/api/v1/users", new { email = "anon@example.com", name = "Anon" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(ct)).Should().BeEmpty(); // empty 401 (/backend-development → Contract Tests)
    }
}
```

---

## Coverage Configuration

coverlet.msbuild, run through the VSTest mode of `dotnet test` (coverlet.msbuild does not support Microsoft Testing Platform). The settings live in one committed file; the generated-file filters are the only path filters (Coverage Exclusions).

```xml
<!-- tests/Directory.Build.props: applies to every project under tests/. -->
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))"
          Condition="'$([MSBuild]::GetPathOfFileAbove(`Directory.Build.props`, `$(MSBuildThisFileDirectory)../`))' != ''" />
  <PropertyGroup>
    <CollectCoverage>true</CollectCoverage>
    <CoverletOutput>$(MSBuildThisFileDirectory)../artifacts/coverage/</CoverletOutput>
    <!-- Coverlet merges only its own json format; cobertura is for the report. -->
    <CoverletOutputFormat>json,cobertura</CoverletOutputFormat>
    <MergeWith>$(MSBuildThisFileDirectory)../artifacts/coverage/coverage.json</MergeWith>
    <!-- Coverage Gate: line and branch, each production module on its own. -->
    <ThresholdType>line,branch</ThresholdType>
    <ThresholdStat>minimum</ThresholdStat>
    <!-- Tool-generated files only. Hand-written code is excluded in source, with a Justification. -->
    <ExcludeByFile>**/*.g.cs,**/Migrations/*.Designer.cs,**/Migrations/*ModelSnapshot.cs</ExcludeByFile>
  </PropertyGroup>
</Project>
```

The merged run: the test projects run one at a time, each merging into the same result, and only the final run carries `Threshold`, so the gate is checked against the merged result rather than a partial one. Verify once, with a deliberately untested branch, that the pipeline fails as expected.

```bash
# bash 4+ (CI). Unit and integration test projects in one merged run.
# Every test project under tests/, at any depth (layout (b) nests module tests under tests/Modules/).
set -euo pipefail
shopt -s globstar nullglob
rm -rf artifacts/coverage
projects=(tests/**/*.csproj)
[ ${#projects[@]} -gt 0 ] || { echo 'no test projects found'; exit 1; }
last=$(( ${#projects[@]} - 1 ))
for i in "${!projects[@]}"; do
  if [ "$i" -eq "$last" ]; then
    dotnet test "${projects[$i]}" -p:Threshold=90   # fails the build below the gate
  else
    dotnet test "${projects[$i]}"
  fi
done
```

A data-only type leaves the denominator in source, where review sees it:

```csharp
// C#. Data-only contract type: auto-properties only (Coverage Exclusions).
[ExcludeFromCodeCoverage(Justification = "Data-only DTO: auto-properties only")]
public sealed record UserDto(Guid Id, string Email, string Name);
```
