using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.VerifyRegistration;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The second step of verify-first registration: a code is checked against the
/// pending row under its lock, and nothing is consumed, created, or looked up
/// by address.
/// </summary>
public class VerifyRegistrationCommandHandlerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Mock<IPendingRegistrationRepository> _pendingRegistrations = new();
    private readonly VerifyRegistrationCommandHandler _handler;

    public VerifyRegistrationCommandHandlerTests()
    {
        _handler = new VerifyRegistrationCommandHandler(
            _pendingRegistrations.Object,
            new Mock<ILogger<VerifyRegistrationCommandHandler>>().Object);
    }

    [Fact]
    public void VerifyIgnoresTheDoor()
    {
        // The registration policy is applied when the row is created and again
        // at completion. A door here would only tell a caller with a code
        // something about the server, and a caller with a code has already
        // proved the address. Held by construction: the handler cannot read the
        // setting, because nothing hands it in.
        var dependencies = typeof(VerifyRegistrationCommandHandler)
            .GetConstructors().Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        dependencies.Should().NotContain(typeof(IOptionsSnapshot<RegistrationSettings>));
        dependencies.Should().NotContain(typeof(IOptions<RegistrationSettings>));
        dependencies.Should().NotContain(typeof(RegistrationSettings));
    }

    [Fact]
    public void VerifyLooksNothingUpByAddress_AndCreatesNothing()
    {
        // No user repository, no address, no password hasher: the handle
        // names the row and the code is the proof. A row minted for a taken
        // address carries a code that was never sent, so a guess against it
        // fails exactly as a guess against a free address's code does.
        var dependencies = typeof(VerifyRegistrationCommandHandler)
            .GetConstructors().Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        dependencies.Should().Equal(
            [typeof(IPendingRegistrationRepository), typeof(ILogger<VerifyRegistrationCommandHandler>)],
            "the handler must not be able to reach a user, a tombstone, or a password");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("")]
    [InlineData("      ")]
    public async Task AMalformedCode_IsRefusedBeforeTheLock(string otp)
    {
        var result = await _handler.Handle(new VerifyRegistrationCommand("handle-1", otp), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOtpFormat.Code);
        _pendingRegistrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoLiveRowUnderTheHandle_IsAnInvalidOrExpiredCode()
    {
        // Unknown, expired and consumed rows all come back as NotFound from the
        // locked read — the repository enforces expiry on that read, so an
        // expired row is refused here before any sweep runs — and all three
        // get one answer. Telling them apart is the enumeration the handle
        // exists to prevent.
        _pendingRegistrations
            .Setup(r => r.CheckCodeUnderLockAsync("handle-1", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.NotFound, Row: null));

        var result = await _handler.Handle(new VerifyRegistrationCommand("handle-1", "123456"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);
    }

    [Fact]
    public async Task AnExhaustedCode_IsTooManyAttempts()
    {
        var row = Row(attempts: PendingRegistration.MaxAttempts);
        _pendingRegistrations
            .Setup(r => r.CheckCodeUnderLockAsync("handle-1", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Exhausted, row));

        var result = await _handler.Handle(new VerifyRegistrationCommand("handle-1", "123456"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.TooManyAttempts.Code,
            "named apart from a wrong code so the screen can offer a new code instead of another guess");
    }

    [Fact]
    public async Task AWrongCode_IsAnInvalidOrExpiredCode_AndTheAttemptWasChargedByTheRepository()
    {
        var row = Row(attempts: 1);
        _pendingRegistrations
            .Setup(r => r.CheckCodeUnderLockAsync("handle-1", "000000", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Wrong, row));

        var result = await _handler.Handle(new VerifyRegistrationCommand("handle-1", "000000"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);

        // The increment lives under the repository's lock, in the same
        // transaction as the comparison. The handler must not add a second
        // one, or a wrong guess would cost two attempts.
        _pendingRegistrations.Verify(
            r => r.CheckCodeUnderLockAsync("handle-1", "000000", It.IsAny<CancellationToken>()), Times.Once);
        _pendingRegistrations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyChecksWithoutConsuming()
    {
        var row = Row(attempts: 0);
        _pendingRegistrations
            .Setup(r => r.CheckCodeUnderLockAsync("handle-1", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Match, row));

        var result = await _handler.Handle(new VerifyRegistrationCommand("handle-1", "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();

        // Consumption means a Users row exists, and none does after a check.
        // The same code is presented again at completion, which consumes.
        _pendingRegistrations.Verify(
            r => r.ConsumeByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _pendingRegistrations.Verify(
            r => r.StartAsync(It.IsAny<PendingRegistrationStartRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        row.IsConsumed.Should().BeFalse();
    }

    private static PendingRegistration Row(int attempts)
    {
        var row = PendingRegistration.Create("handle-1", "jane@one.example", Now);
        row.TryChargeMailWindow(TimeSpan.FromSeconds(60), 3, Now);
        row.IssueCode("hash-of-123456", 5, "en", Now);
        for (var i = 0; i < attempts; i++) row.RecordFailedAttempt();
        return row;
    }
}
