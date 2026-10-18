using System.Net;
using System.Threading.RateLimiting;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ClassifyTokenClient;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Common;
using Auth_API.Common.Middleware;
using Auth_API.Common.RateLimiting;
using Auth_API.Tests.Authentication.ClientCredentials;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The token budget per application (OI-178), as a server meets it: requests through the real
/// <see cref="TokenClientClassificationMiddleware"/>, the real
/// <see cref="ClassifyTokenClientQueryHandler"/> behind a real mediator, the real
/// <see cref="ClientAuthenticator"/> with real ES256 assertions, the real
/// <see cref="TokenAssertionFailureBudget"/>, and the "token" policy partitioned by the real
/// <see cref="TokenRequestClass"/>, as Program.cs partitions it. The endpoint stands in for the
/// grant: it authenticates the client the way every grant does, after the limiter, spending the
/// assertion's jti. Only the key and jti tables are stubs.
/// </summary>
/// <remarks>
/// Program.cs's wiring (the middleware just before the limiter, the policy body and its
/// settings) is pinned by <see cref="Configuration.TokenBudgetPerApplicationGuardTests"/>.
/// </remarks>
public sealed class TokenBudgetPerApplicationTests : IAsyncLifetime
{
    private const string TokenPath = "/api/v1/auth/token";
    private const string Server = "203.0.113.7";
    private const string Forger = "198.51.100.66";
    private const int PerAddress = 3;
    private const int PerApplication = 8;

    private readonly MachineCredentials _signInKey = new();
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Mock<IApplicationMachineKeyRepository> _keys = new();
    private readonly InMemoryJtis _jtis = new();
    private int _settingsVersion;
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kid, CancellationToken _) => kid == _signInKey.Kid ? SignInKey() : null);
        _keys.Setup(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        (_host, _client) = await StartHostAsync();
    }

    private MachineKeyCredential? SignInKey() => new(new ApplicationMachineKey(
        Guid.NewGuid(), _applicationId, _signInKey.Kid, null, MachineClients.SigningAlgorithm,
        _signInKey.PublicKey.PublicKeyJwk, DateTime.UtcNow.AddDays(-1), Guid.NewGuid(), null, null, null, null,
        MachineKeyPurpose.SignIn), MachineCredentials.ClientId);

    /// <summary>
    /// One API process: its own failure allowance, its own record of the assertions counted, its own
    /// buckets; the key and jti tables are the database every process shares.
    /// </summary>
    private async Task<(IHost Host, HttpClient Client)> StartHostAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ClassifyTokenClientQueryHandler>());
                    services.AddSingleton(_keys.Object);
                    services.AddSingleton<IClientAssertionJtiStore>(_jtis);
                    services.AddSingleton<IClientAssertionVerifier>(new ClientAssertionVerifier(TimeProvider.System));
                    services.AddSingleton<Microsoft.Extensions.Options.IOptionsSnapshot<JwtSettings>>(TestHelpers.CreateOptions(new JwtSettings { Issuer = MachineCredentials.Issuer }));
                    services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
                    services.AddScoped<IClientAuthenticator, ClientAuthenticator>();
                    services.AddSingleton(_ => new TokenAssertionFailureBudget(
                        () => _settingsVersion, () => new FixedWindowBudget(PerAddress, 3600)));
                    services.AddSingleton(new TokenAssertionSightings(TimeProvider.System));
                    services.AddRateLimiter(options =>
                    {
                        // As Program.cs registers it.
                        options.AddPolicy("token", httpContext =>
                            TokenRequestClass.Of(httpContext).Partition(
                                _settingsVersion,
                                ClientIpResolver.Resolve(httpContext) ?? "unknown",
                                perAddress: () => new FixedWindowBudget(PerAddress, 3600),
                                perApplication: () => new FixedWindowBudget(PerApplication, 3600)));
                        options.OnRejected = (context, _) =>
                        {
                            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                            return ValueTask.CompletedTask;
                        };
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseMiddleware<TokenClientClassificationMiddleware>();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints => endpoints
                        .MapPost(TokenPath, TheGrant)
                        .WithMetadata(new ClassifiesTokenClientAttribute())
                        .RequireRateLimiting("token"));
                }))
            .StartAsync();
        return (host, host.GetTestClient());
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _signInKey.Dispose();
    }

    /// <summary>What every grant does with an assertion, after the limiter: authenticate, spending the jti.</summary>
    private static async Task<IResult> TheGrant(HttpContext context, IClientAuthenticator authenticator)
    {
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync();
        }
        catch (InvalidDataException)
        {
            // What model binding answers for a form it cannot read.
            return Results.BadRequest();
        }

        if (string.IsNullOrEmpty(form["client_assertion"]))
        {
            return Results.BadRequest();
        }

        // As the real refresh grant does: an unknown token is refused before the client is
        // authenticated, so the assertion's jti is not spent.
        if (form["refresh_token"] == "unknown")
        {
            return Results.BadRequest();
        }

        var authenticated = await authenticator.AuthenticateAsync(
            new ClientAssertionRequest(form["client_assertion_type"], form["client_assertion"],
                string.IsNullOrEmpty(form["client_id"]) ? null : form["client_id"].ToString(), ClientIpResolver.Resolve(context)),
            MachineKeyPurpose.SignIn, CancellationToken.None);
        return authenticated.IsError ? Results.BadRequest() : Results.Ok();
    }

    private Task<HttpResponseMessage> TokenAsync(
        string address, string? assertion, string grantType = "refresh_token", string refreshToken = "r") =>
        TokenAsync(_client, address, assertion, grantType, refreshToken);

    private static Task<HttpResponseMessage> TokenAsync(
        HttpClient client, string address, string? assertion, string grantType = "refresh_token", string refreshToken = "r")
    {
        var fields = new Dictionary<string, string> { ["grant_type"] = grantType, ["refresh_token"] = refreshToken };
        if (assertion is not null)
        {
            fields["client_assertion_type"] = ClientAuthenticator.JwtBearerAssertionType;
            fields["client_assertion"] = assertion;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, TokenPath) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        return client.SendAsync(request);
    }

    private int KeyReads() => _keys.Invocations.Count(i => i.Method.Name == nameof(IApplicationMachineKeyRepository.GetActiveByKidAsync));

    // ===== TB1: a verified application counts on its own budget =====

    [Fact]
    public async Task TB1_AVerifiedServer_IsNotHeldToItsAddressesBudget_ButToItsApplications()
    {
        for (var i = 0; i < PerApplication; i++)
        {
            (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK,
                "request {0} is within the application's budget, though past the address's {1}", i + 1, PerAddress);
        }

        (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the application's own budget still binds");
    }

    [Fact]
    public async Task TB1_ABurstOfValidRenewalsFromOneAddress_IsNotCappedByConcurrency()
    {
        // Review finding (F1): the places running verifications hold were counted against the
        // allowance of assertions that do not verify, so a server renewing more people at one
        // instant than that allowance was refused while nothing had failed.
        _keys.Setup(r => r.GetActiveByKidAsync(_signInKey.Kid, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(200);
                return SignInKey();
            });

        var answers = await Task.WhenAll(
            Enumerable.Range(0, PerAddress * 2).Select(_ => TokenAsync(Server, _signInKey.Assertion())));

        answers.Select(answer => answer.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK,
            "nothing failed, and the application's budget ({0}) has room for {1} at once", PerApplication, PerAddress * 2);
    }

    // ===== TB2: junk assertions naming the application cannot spend its budget =====

    [Fact]
    public async Task TB2_ForgedAssertionsNamingTheApplication_SpendOnlyTheForgersAddress_AndStopBeingVerified()
    {
        var junk = Junk();
        for (var i = 0; i < PerAddress; i++)
        {
            (await TokenAsync(Forger, junk)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "a forged assertion is refused as an invalid client, on the forger's address");
        }

        var readsWhenSpent = KeyReads();
        for (var i = 0; i < 20; i++)
        {
            (await TokenAsync(Forger, junk)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        KeyReads().Should().Be(readsWhenSpent, "an address past its allowance of bad assertions causes no more verification");

        for (var i = 0; i < PerApplication; i++)
        {
            (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK,
                "the application's budget is whole: only a verified assertion is counted in it");
        }
    }

    /// <summary>An assertion that names the application but is signed by another key.</summary>
    private static string Junk()
    {
        using var other = new MachineCredentials();
        return other.Assertion();
    }

    // ===== TB3: a captured assertion replayed counts on the replayer's address =====

    [Fact]
    public async Task TB3_AReplayedAssertion_IsNotCountedAsTheApplication()
    {
        var captured = _signInKey.Assertion();
        (await TokenAsync(Server, captured)).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < PerApplication + 2; i++)
        {
            (await TokenAsync(Forger, captured)).StatusCode.Should().NotBe(HttpStatusCode.OK, "a jti is accepted once");
        }

        for (var i = 1; i < PerApplication; i++)
        {
            (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK,
                "replays of a spent assertion took nothing from the application's budget");
        }
    }

    [Fact]
    public async Task TB3_AnUnspentAssertionReplayedIntoAGrantThatRefusesFirst_CountsOnTheApplicationOnce()
    {
        // Review finding: a grant may refuse before it spends the jti (here, an unknown refresh
        // token), so the jti check alone would let one captured, still-unspent assertion be
        // counted on the application's budget on every copy.
        var captured = _signInKey.Assertion();
        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < PerApplication * 2; i++)
        {
            answers.Add((await TokenAsync(Forger, captured, refreshToken: "unknown")).StatusCode);
        }

        answers.Should().NotContain(HttpStatusCode.OK);
        answers[^1].Should().Be(HttpStatusCode.TooManyRequests);
        // Review finding (F2): each copy is charged on the replayer's address like an assertion
        // that does not verify, so past the allowance the copies are not verified at all.
        KeyReads().Should().Be(1 + PerAddress, "the first sighting, then one copy per place in the allowance");

        for (var i = 1; i < PerApplication; i++)
        {
            (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK,
                "the copies after the first counted on the replayer's address, not on the application");
        }
    }

    [Fact]
    public async Task TB3_ASpentAssertionReplayedToAnotherProcess_IsNotCountedAsTheApplication()
    {
        // Review finding (F5): the record of assertions counted lives in one process. On another
        // API instance, or after a restart, only the read of the spent jti keeps a replay of a
        // used assertion off the application's budget.
        var captured = _signInKey.Assertion();
        (await TokenAsync(Server, captured)).StatusCode.Should().Be(HttpStatusCode.OK, "the grant spends the jti");

        var (other, otherClient) = await StartHostAsync();
        try
        {
            for (var i = 0; i < PerApplication + 2; i++)
            {
                (await TokenAsync(otherClient, Forger, captured)).StatusCode.Should().NotBe(HttpStatusCode.OK);
            }

            for (var i = 0; i < PerApplication; i++)
            {
                (await TokenAsync(otherClient, Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK,
                    "the replays took nothing from the application's budget in this process either");
            }
        }
        finally
        {
            otherClient.Dispose();
            await other.StopAsync();
            other.Dispose();
        }
    }

    [Fact]
    public async Task TB2_AfterAFirstFailure_AConcurrentBurstOfForgedAssertions_IsVerifiedAtMostTheAllowanceAtATime()
    {
        // Review finding: checking the allowance before verifying and counting the failure after
        // let a concurrent burst from one address pass the check together. Once the address has
        // failed in the window (F1), every running verification holds its place.
        var junk = Junk();
        (await TokenAsync(Forger, junk)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "the address's first failure");
        _keys.Setup(r => r.GetActiveByKidAsync(It.Is<string>(kid => kid != _signInKey.Kid), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(200);
                return null;
            });

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => TokenAsync(Forger, junk)));

        KeyReads().Should().BeLessThanOrEqualTo(PerAddress,
            "a verification in progress holds its place in the allowance of an address that has failed");
    }

    [Fact]
    public async Task TB2_NoForwardedAddress_SpellsAnApplicationsPartition()
    {
        // Review finding: an address key is "v{version}:" plus whatever the resolver read, so an
        // application key built the same way could be named by a request that reaches the API
        // with its own X-Forwarded-For.
        for (var i = 0; i < PerAddress + 2; i++)
        {
            await TokenAsync($"app:{_applicationId:N}", assertion: null);
        }

        for (var i = 0; i < PerApplication; i++)
        {
            (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK,
                "requests without a key never land in the application's partition");
        }
    }

    // ===== TB4: requests without an assertion keep the per-address budget =====

    [Fact]
    public async Task TB4_RequestsWithoutAnAssertion_KeepThePerAddressBudget()
    {
        for (var i = 0; i < PerAddress; i++)
        {
            (await TokenAsync(Forger, assertion: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        (await TokenAsync(Forger, assertion: null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await TokenAsync(Server, assertion: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "one address cannot spend another's budget");
    }

    // ===== TB6: one verification per request =====

    [Fact]
    public async Task TB6_AnAuthenticatedRequest_ReadsTheKeyAndSpendsTheJti_OnceEach()
    {
        const int Requests = 5;
        for (var i = 0; i < Requests; i++)
        {
            (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        KeyReads().Should().Be(Requests, "the grant reuses the verification made before the limiter");
        _jtis.Spent.Should().HaveCount(Requests);
        _jtis.Reads.Should().Be(Requests, "one read-only check per request, before the limiter");
    }

    // ===== TB7: a saved limit applies to fresh partitions =====

    [Fact]
    public async Task TB7_ANewSettingsVersion_OpensFreshPartitions()
    {
        for (var i = 0; i < PerApplication; i++)
        {
            await TokenAsync(Server, _signInKey.Assertion());
        }

        (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        Interlocked.Increment(ref _settingsVersion);

        (await TokenAsync(Server, _signInKey.Assertion())).StatusCode.Should().Be(HttpStatusCode.OK,
            "a saved limit reaches new partitions through the settings-version stamp");
    }

    [Fact]
    public async Task AnAssertionOnAGrantThatTakesNone_IsCountedOnTheAddress()
    {
        (await TokenAsync(Server, _signInKey.Assertion(), grantType: "password")).StatusCode
            .Should().Be(HttpStatusCode.OK, "the stand-in grant still authenticates; what matters is the bucket");

        for (var i = 1; i < PerAddress; i++)
        {
            await TokenAsync(Server, assertion: null);
        }

        (await TokenAsync(Server, assertion: null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the request on an unknown grant was counted on the address, not the application");
    }

    [Fact]
    public async Task AFormThatCannotBeRead_IsCountedOnTheAddress_WithoutFailingTheRequest()
    {
        // More fields than the form reader accepts (1,024): the classification leaves the request
        // to model binding, which refuses it, and counts it on its address like any other.
        var fields = Enumerable.Range(0, 1100).ToDictionary(i => $"f{i}", _ => "1");
        fields["client_assertion"] = _signInKey.Assertion();

        for (var i = 0; i < PerAddress; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, TokenPath) { Content = new FormUrlEncodedContent(fields) };
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", Forger);
            (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        (await TokenAsync(Forger, assertion: null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        KeyReads().Should().Be(0, "nothing was verified");
    }

    /// <summary>The ClientAssertionJtis table: single use by the write, and the read-only check.</summary>
    private sealed class InMemoryJtis : IClientAssertionJtiStore
    {
        private readonly HashSet<(Guid, string)> _spent = [];
        private int _reads;

        public IReadOnlyCollection<(Guid, string)> Spent { get { lock (_spent) { return _spent.ToList(); } } }
        public int Reads => Volatile.Read(ref _reads);

        public Task<bool> TryConsumeAsync(Guid applicationId, string jti, DateTime expiresAtUtc, CancellationToken cancellationToken)
        {
            lock (_spent)
            {
                return Task.FromResult(_spent.Add((applicationId, jti)));
            }
        }

        public Task<bool> IsSpentAsync(Guid applicationId, string jti, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            lock (_spent)
            {
                return Task.FromResult(_spent.Contains((applicationId, jti)));
            }
        }

        public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
