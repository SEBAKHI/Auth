using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Infrastructure.Configuration;
using Auth.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security;

/// <summary>
/// Tests for the breached-password policy decision (enabled/disabled, Enforce/Warn, threshold, fail-open/closed).
/// </summary>
public class PasswordBreachEvaluatorTests
{
    private const string Password = "Password1!";

    // The operator's log search and S09 grep these prefixes; the placeholder sits right after "failed".
    private const string TimeoutPrefix = "Breached-password check failed (Timeout)";
    private const string ErrorPrefix = "Breached-password check failed (Error)";

    private static PasswordBreachEvaluator CreateEvaluator(
        BreachedPasswordCheckSettings settings,
        Mock<IBreachedPasswordChecker> checker,
        IPasswordWarningContext warnings,
        Mock<ILogger<PasswordBreachEvaluator>>? logger = null) =>
        CreateEvaluator(settings, checker.Object, warnings, logger);

    private static PasswordBreachEvaluator CreateEvaluator(
        BreachedPasswordCheckSettings settings,
        IBreachedPasswordChecker checker,
        IPasswordWarningContext warnings,
        Mock<ILogger<PasswordBreachEvaluator>>? logger = null)
    {
        var passwordSettings = new PasswordSettings { BreachedPasswordCheck = settings };
        return new PasswordBreachEvaluator(
            checker,
            warnings,
            TestHelpers.CreateOptions(passwordSettings),
            logger?.Object ?? NullLogger<PasswordBreachEvaluator>.Instance);
    }

    private static void VerifyLogged(
        Mock<ILogger<PasswordBreachEvaluator>> logger, LogLevel level, string prefix, Times times) =>
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.StartsWith(prefix, StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    private static void VerifyNothingLoggedAtOrAbove(Mock<ILogger<PasswordBreachEvaluator>> logger, LogLevel level) =>
        logger.Verify(
            l => l.Log(
                It.Is<LogLevel>(logged => logged >= level),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

    [Fact]
    public async Task Disabled_ReturnsSuccess_AndDoesNotCallChecker()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        var warnings = new PasswordWarningContext();
        var evaluator = CreateEvaluator(new BreachedPasswordCheckSettings { Enabled = false }, checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse();
        warnings.Warnings.Should().BeEmpty();
        checker.Verify(
            x => x.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// T1 (S09): a deployment with no override runs the shipped defaults, and they refuse a
    /// password the corpus lists.
    /// </summary>
    [Fact]
    public async Task Defaults_RejectBreached()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var warnings = new PasswordWarningContext();
        var evaluator = CreateEvaluator(new BreachedPasswordCheckSettings(), checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.PasswordBreached");
        warnings.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// T4 (S09): switching the check on or off in the console takes effect on the next request,
    /// in both directions, with no restart. The value travels the production path: the database
    /// overrides provider reloads, the options rebind, and each request's DI scope reads the value
    /// in force when it began. The token the checker receives is the caller's own.
    /// </summary>
    [Fact]
    public async Task EnabledReload_NextScopeScreens()
    {
        var overrides = new List<(string SectionKey, string OverridesJson)>
        {
            ("Password", """{"BreachedPasswordCheck":{"Enabled":false}}"""),
        };
        var provider = new DbSettingsConfigurationProvider(() => [.. overrides], new Dictionary<string, int>());
        var configuration = new ConfigurationBuilder().Add(new FixedProviderSource(provider)).Build();

        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.Configure<PasswordSettings>(configuration.GetSection(PasswordSettings.SectionName));
        services.AddScoped<IPasswordWarningContext, PasswordWarningContext>();
        services.AddScoped<IPasswordBreachEvaluator, PasswordBreachEvaluator>();
        services.AddSingleton(checker.Object);
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var caller = new CancellationTokenSource();

        async Task<ErrorOr.ErrorOr<ErrorOr.Success>> NextRequestAsync()
        {
            await using var scope = root.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPasswordBreachEvaluator>()
                .EvaluateAsync(Password, caller.Token);
        }

        // Off: the request makes no external call.
        (await NextRequestAsync()).IsError.Should().BeFalse();
        checker.Verify(x => x.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Switched on and saved: the next request screens, with the caller's token.
        overrides[0] = ("Password", """{"BreachedPasswordCheck":{"Enabled":true}}""");
        provider.Reload();

        var screened = await NextRequestAsync();
        screened.IsError.Should().BeTrue();
        screened.FirstError.Code.Should().Be("User.PasswordBreached");
        checker.Verify(x => x.GetBreachCountAsync(Password, caller.Token), Times.Once);

        // Switched off again: the next request is back to no call.
        overrides[0] = ("Password", """{"BreachedPasswordCheck":{"Enabled":false}}""");
        provider.Reload();

        (await NextRequestAsync()).IsError.Should().BeFalse();
        checker.Verify(x => x.GetBreachCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A configuration source that hands the builder an existing provider.</summary>
    private sealed class FixedProviderSource(IConfigurationProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    [Fact]
    public async Task Enforce_Breached_ReturnsError()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(42);
        var warnings = new PasswordWarningContext();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, Mode = BreachAction.Enforce, RejectThreshold = 1 },
            checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.PasswordBreached");
        warnings.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Warn_Breached_ReturnsSuccess_AndRecordsWarning()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(42);
        var warnings = new PasswordWarningContext();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, Mode = BreachAction.Warn, RejectThreshold = 1 },
            checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse();
        warnings.Warnings.Should().ContainSingle();
        warnings.Warnings[0].Code.Should().Be("User.PasswordBreached");
    }

    [Fact]
    public async Task NotBreached_ReturnsSuccess_NoWarning()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var warnings = new PasswordWarningContext();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, Mode = BreachAction.Enforce, RejectThreshold = 1 },
            checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse();
        warnings.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task BelowThreshold_ReturnsSuccess()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var warnings = new PasswordWarningContext();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, Mode = BreachAction.Enforce, RejectThreshold = 10 },
            checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ServiceFailure_FailOpen_ReturnsSuccess()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HIBP down"));
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = true }, checker, warnings, logger);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyLogged(logger, LogLevel.Warning, ErrorPrefix, Times.Once());
    }

    [Fact]
    public async Task ServiceFailure_FailClosed_ReturnsError()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HIBP down"));
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = false }, checker, warnings, logger);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.PasswordBreachCheckUnavailable");
        VerifyLogged(logger, LogLevel.Error, ErrorPrefix, Times.Once());
    }

    /// <summary>
    /// The shape <see cref="HttpClient.Timeout"/> throws: a <see cref="TaskCanceledException"/> whose
    /// inner exception is a <see cref="TimeoutException"/>, while the caller's token is still live.
    /// </summary>
    private static TaskCanceledException HttpClientTimeout() =>
        new("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());

    [Fact]
    public async Task Timeout_FailOpen_ReturnsSuccess_AndLogsWarning()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(HttpClientTimeout());
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = true }, checker, warnings, logger);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse();
        warnings.Warnings.Should().BeEmpty();
        VerifyLogged(logger, LogLevel.Warning, TimeoutPrefix, Times.Once());
    }

    [Fact]
    public async Task Timeout_FailClosed_ReturnsBreachCheckUnavailable()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(HttpClientTimeout());
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = false }, checker, warnings, logger);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.PasswordBreachCheckUnavailable");
        VerifyLogged(logger, LogLevel.Error, TimeoutPrefix, Times.Once());
    }

    /// <summary>
    /// A deadline other than HttpClient.Timeout (S09's per-call timeout) cancels with no
    /// TimeoutException inside. The caller did not cancel, so it is still a timeout and still
    /// follows FailOpen.
    /// </summary>
    [Fact]
    public async Task Deadline_NoInnerException_FailOpen_ReturnsSuccess()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = true }, checker, warnings, logger);
        using var caller = new CancellationTokenSource();

        var result = await evaluator.EvaluateAsync(Password, caller.Token);

        result.IsError.Should().BeFalse();
        VerifyLogged(logger, LogLevel.Warning, TimeoutPrefix, Times.Once());
    }

    /// <summary>
    /// The caller's own cancellation (the client went away) is neither a fail-open success, which
    /// would let the handler go on to write the password for an abandoned request, nor a fail-closed
    /// error: it propagates, and nothing is logged as a failure of the check.
    /// </summary>
    [Fact]
    public async Task CallerCancelled_PropagatesOperationCanceledException()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(caller.Token));
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = true }, checker, warnings, logger);

        var act = () => evaluator.EvaluateAsync(Password, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        VerifyNothingLoggedAtOrAbove(logger, LogLevel.Warning);
    }

    /// <summary>
    /// No log line carries the breach count, in either mode: a high count all but names the
    /// password in HIBP's public corpus, and in Warn mode it is the password the account now holds.
    /// </summary>
    [Theory]
    [InlineData(BreachAction.Enforce)]
    [InlineData(BreachAction.Warn)]
    public async Task Breached_NoLogLineCarriesTheCount(BreachAction mode)
    {
        const int count = 28191;
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(count);
        var logger = new CapturingLogger<PasswordBreachEvaluator>();
        var evaluator = new PasswordBreachEvaluator(
            checker.Object,
            new PasswordWarningContext(),
            TestHelpers.CreateOptions(new PasswordSettings
            {
                BreachedPasswordCheck = new BreachedPasswordCheckSettings { Mode = mode },
            }),
            logger);

        await evaluator.EvaluateAsync(Password, CancellationToken.None);

        logger.Entries.Should().NotBeEmpty("the decision is still logged, without the count");
        logger.Entries.Should().NotContain(entry =>
            entry.Message.Contains("28191", StringComparison.Ordinal) ||
            entry.Values.Contains("28191"));
    }

    /// <summary>
    /// A success answer that is not an HIBP listing (here a proxy's block page) is a failure of
    /// the service, not "not breached": with FailOpen it passes with a Warning "(Error)" the
    /// operator's count finds; fail-closed refuses with the existing code.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SuccessWithNoListing_FollowsFailOpen_AsError(bool failOpen)
    {
        var handler = new StaticResponseHttpMessageHandler(
            System.Net.HttpStatusCode.OK, "<html><body>Blocked by policy</body></html>", "text/html");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.pwnedpasswords.com/") };
        var checker = new HibpBreachedPasswordChecker(client, TestHelpers.CreateOptions(new PasswordSettings()));
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = failOpen },
            checker, new PasswordWarningContext(), logger);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        if (failOpen)
        {
            result.IsError.Should().BeFalse();
            VerifyLogged(logger, LogLevel.Warning, ErrorPrefix, Times.Once());
        }
        else
        {
            result.IsError.Should().BeTrue();
            result.FirstError.Code.Should().Be("User.PasswordBreachCheckUnavailable");
            VerifyLogged(logger, LogLevel.Error, ErrorPrefix, Times.Once());
        }
    }

    /// <summary>
    /// A RejectThreshold of 0 from a file or a direct database write (the console refuses it)
    /// would count every password as breached and refuse them all. The applied value is clamped.
    /// </summary>
    [Fact]
    public async Task RejectThresholdZero_IsClampedToOne()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, Mode = BreachAction.Enforce, RejectThreshold = 0 },
            checker, new PasswordWarningContext());

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse("a password found in no breach is never refused");
    }

    /// <summary>
    /// OI-98: the client goes away while a transport failure is already in flight. The failure
    /// is not a cancellation, so the filter catches it, but the caller's cancellation wins: no
    /// fail-open success for the abandoned request, and no warning about a check nobody awaits.
    /// </summary>
    [Fact]
    public async Task CallerCancelledDuringTransportFailure_PropagatesCancellation_NoWarning()
    {
        using var caller = new CancellationTokenSource();
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await caller.CancelAsync();
                throw new HttpRequestException("connection reset");
            });
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = true }, checker, warnings, logger);

        var act = () => evaluator.EvaluateAsync(Password, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        VerifyNothingLoggedAtOrAbove(logger, LogLevel.Warning);
    }

    /// <summary>
    /// Guard against the shape of the real runtime exception: a real HIBP checker on a real
    /// <see cref="HttpClient"/> whose <see cref="HttpClient.Timeout"/> elapses (no network) reaches
    /// fail-open, not the error contract.
    /// </summary>
    [Fact]
    public async Task RealHttpClientTimeout_FailOpen_ReturnsSuccess()
    {
        var handler = new HangingHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        var checker = new HibpBreachedPasswordChecker(client, TestHelpers.CreateOptions(new PasswordSettings()));
        var warnings = new PasswordWarningContext();
        var logger = new Mock<ILogger<PasswordBreachEvaluator>>();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = true }, checker, warnings, logger);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        handler.Requests.Should().Be(1);
        result.IsError.Should().BeFalse();
        VerifyLogged(logger, LogLevel.Warning, TimeoutPrefix, Times.Once());
    }
}
