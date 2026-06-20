using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Security;

/// <summary>
/// Tests for the breached-password policy decision (enabled/disabled, Enforce/Warn, threshold, fail-open/closed).
/// </summary>
public class PasswordBreachEvaluatorTests
{
    private const string Password = "Password1!";

    private static PasswordBreachEvaluator CreateEvaluator(
        BreachedPasswordCheckSettings settings,
        Mock<IBreachedPasswordChecker> checker,
        IPasswordWarningContext warnings)
    {
        var passwordSettings = new PasswordSettings { BreachedPasswordCheck = settings };
        return new PasswordBreachEvaluator(
            checker.Object,
            warnings,
            Options.Create(passwordSettings),
            NullLogger<PasswordBreachEvaluator>.Instance);
    }

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
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = true }, checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ServiceFailure_FailClosed_ReturnsError()
    {
        var checker = new Mock<IBreachedPasswordChecker>();
        checker.Setup(x => x.GetBreachCountAsync(Password, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HIBP down"));
        var warnings = new PasswordWarningContext();
        var evaluator = CreateEvaluator(
            new BreachedPasswordCheckSettings { Enabled = true, FailOpen = false }, checker, warnings);

        var result = await evaluator.EvaluateAsync(Password, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.PasswordBreachCheckUnavailable");
    }
}
