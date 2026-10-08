using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Infrastructure.Security;
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
    /// A deadline other than HttpClient.Timeout (S09's per-call timeout) cancels with no inner
    /// exception. The caller did not cancel, so it is still a timeout and still follows FailOpen.
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
        var checker = new HibpBreachedPasswordChecker(client, NullLogger<HibpBreachedPasswordChecker>.Instance);
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
