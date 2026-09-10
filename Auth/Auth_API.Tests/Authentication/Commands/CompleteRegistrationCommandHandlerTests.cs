using System.Globalization;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.CompleteRegistration;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The third step of verify-first registration. The order is the contract:
/// door, code shape, code under the lock, password, address, hash, then the
/// account and the consumption in one transaction — and after that commit,
/// nothing may fail the request.
/// </summary>
public class CompleteRegistrationCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static CompleteRegistrationCommand Command(
        string otp = "123456", string password = "ValidPass1!", bool createOrganization = false) =>
        new("handle-1", otp, password, "Jane", "Doe", null, createOrganization, "device-1", "203.0.113.7", "TestAgent/1.0");

    // ── The invariant and the door ──────────────────────────────────────────

    [Fact]
    public void TheCommandExposesNoEmailMember()
    {
        // The handle fixes the address. An email member would let the client
        // name one — and a screen that shows the address read-only would be a
        // convention, not a property of the contract.
        typeof(CompleteRegistrationCommand).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Email", StringComparison.OrdinalIgnoreCase));
        typeof(CompleteRegistrationCommand).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Phone", StringComparison.OrdinalIgnoreCase),
                "the phone is written on another connection after the row exists; it returns as a profile edit");
    }

    [Fact]
    public async Task ClosedServer_RefusesBeforeTouchingTheCode()
    {
        var scenario = new Scenario();
        scenario.Registration.AllowSelfRegistration = false;

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.SelfRegistrationClosed");
        scenario.Calls.Should().BeEmpty();
    }

    // ── The code, before anything else ──────────────────────────────────────

    [Theory]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public async Task AMalformedCode_IsRefusedBeforeTheLock(string otp)
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync(Command(otp: otp));

        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOtpFormat.Code);
        scenario.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AWrongCodeAtComplete_NeverHashesThePassword()
    {
        // With a password that would fail the policy: if the policy ran before
        // the code, the answer would be about the password.
        var scenario = new Scenario().CodeAnswers(PendingRegistrationCodeOutcome.Wrong);

        var result = await scenario.RunAsync(Command(password: "weak"));

        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code,
            "the code is answered before the password is so much as looked at");
        scenario.Hasher.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
        scenario.Breach.Verify(b => b.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AWrongCodeAtComplete_TouchesNoUserOrTombstoneLookup()
    {
        var scenario = new Scenario().CodeAnswers(PendingRegistrationCodeOutcome.Wrong);

        await scenario.RunAsync(Command());

        scenario.Calls.Should().Equal("pending.CheckCodeUnderLockAsync");
        scenario.Users.VerifyNoOtherCalls();
        scenario.Tombstones.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AWrongCodeAtComplete_AnswersTheSameForFreeRegisteredAndReservedAddresses()
    {
        // The lookups that would tell the classes apart are never reached.
        var free = new Scenario().CodeAnswers(PendingRegistrationCodeOutcome.Wrong);
        var registered = new Scenario().AddressRegistered().CodeAnswers(PendingRegistrationCodeOutcome.Wrong);
        var reserved = new Scenario().AddressReserved().CodeAnswers(PendingRegistrationCodeOutcome.Wrong);

        var answers = new[]
        {
            await free.RunAsync(Command()),
            await registered.RunAsync(Command()),
            await reserved.RunAsync(Command()),
        };

        answers.Select(a => a.FirstError.Code).Distinct().Should().ContainSingle()
            .Which.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);
        new[] { free, registered, reserved }.Select(s => string.Join(">", s.Calls)).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task AnExpiredOrConsumedRow_IsRefusedAtComplete_BeforeAnyOtherWork()
    {
        // The locked read enforces expiry and consumption, so an expired,
        // consumed or unknown handle comes back as NotFound — and must get
        // the same answer a wrong code gets, before any password work.
        var scenario = new Scenario().CodeAnswers(PendingRegistrationCodeOutcome.NotFound);

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);
        scenario.Calls.Should().Equal(new List<string> { "pending.CheckCodeUnderLockAsync" });
        scenario.Hasher.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
        scenario.Users.Verify(u => u.CreateVerifiedAsync(It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AWrongCodeAndAMissingRow_AnswerAlike()
    {
        var wrong = new Scenario().CodeAnswers(PendingRegistrationCodeOutcome.Wrong);
        var missing = new Scenario().CodeAnswers(PendingRegistrationCodeOutcome.NotFound);

        var forWrong = await wrong.RunAsync(Command());
        var forMissing = await missing.RunAsync(Command());

        forMissing.FirstError.Should().Be(forWrong.FirstError, "expired, consumed and wrong must be one answer");
        missing.Calls.Should().Equal(wrong.Calls);
    }

    [Fact]
    public async Task AnExhaustedCode_IsTooManyAttempts_AndCreatesNothing()
    {
        var scenario = new Scenario().CodeAnswers(PendingRegistrationCodeOutcome.Exhausted);

        var result = await scenario.RunAsync(Command());

        result.FirstError.Code.Should().Be(EmailVerificationErrors.TooManyAttempts.Code);
        scenario.Users.Verify(u => u.CreateVerifiedAsync(It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AttemptsAreSharedAcrossVerifyAndComplete()
    {
        // Both steps check through the one locked primitive, and neither keeps
        // a counter of its own: five wrong codes across the two close the code.
        var root = SolutionDirectory();
        var verify = File.ReadAllText(Path.Combine(root, "Auth.Application", "Features", "Authentication", "VerifyRegistration", "VerifyRegistrationCommandHandler.cs"));
        var complete = File.ReadAllText(Path.Combine(root, "Auth.Application", "Features", "Authentication", "CompleteRegistration", "CompleteRegistrationCommandHandler.cs"));

        foreach (var (name, source) in new[] { ("verify", verify), ("complete", complete) })
        {
            source.Should().Contain("CheckCodeUnderLockAsync(", $"the {name} step checks under the row's lock");
            source.Should().NotContain("RecordFailedAttempt", $"the {name} step keeps no counter of its own");
            source.Should().NotMatchRegex(@"AttemptCount\s*(\+\+|\+=|-=|=[^=])", $"the {name} step never writes the counter; reading it for a log line is fine");
            source.Should().NotContain("IncrementAttempt", $"the {name} step never charges outside the locked primitive");
        }
    }

    // ── The order ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TheOrderIsPinned_CodeThenPasswordThenAddressThenHashThenTheAccount()
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync(Command(createOrganization: true));

        result.IsError.Should().BeFalse();
        scenario.Calls.Should().Equal(
            "pending.CheckCodeUnderLockAsync",
            "breach.EvaluateAsync",
            "users.ExistsByEmailAsync",
            "tombstones.ExistsByEmailHashAsync",
            "hasher.HashPassword",
            "users.CreateVerifiedAsync",
            "events.DispatchEventsAsync",
            "organization.CreateAsync",
            "session.BuildAsync",
            "events.DispatchEventsAsync");
    }

    [Fact]
    public async Task AnExistingAccount_IsADuplicate_AfterTheProof_AndBeforeTheHash()
    {
        var scenario = new Scenario().AddressRegistered();

        var result = await scenario.RunAsync(Command());

        result.FirstError.Code.Should().Be("User.DuplicateEmail");
        scenario.Users.Verify(u => u.ExistsByEmailAsync("jane@one.example", It.IsAny<CancellationToken>()), Times.Once,
            "the address checked is the row's, never anything from the request");
        scenario.Hasher.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
        scenario.Users.Verify(u => u.CreateVerifiedAsync(It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteRefusesAReservedIdentifier_EvenWithTheRightCode()
    {
        var scenario = new Scenario().AddressReserved();

        var result = await scenario.RunAsync(Command());

        result.FirstError.Code.Should().Be("User.DuplicateEmail", "the same conflict an ordinary duplicate gets; the caller proved the address, there is nothing left to hide");
        scenario.Tombstones.Verify(t => t.ExistsByEmailHashAsync("hash:jane@one.example", It.IsAny<CancellationToken>()), Times.Once,
            "the tombstone checked is the row's address; nothing in the database enforces tombstones, so a wrong argument here would void the reservation");
        scenario.Hasher.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
        scenario.Users.Verify(u => u.CreateVerifiedAsync(It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AWeakPassword_IsRefused_AfterTheCode_AndBeforeAnyLookup()
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync(Command(password: "weak"));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().NotBe(EmailVerificationErrors.InvalidOrExpiredOtp.Code, "the code was right; the password is what failed");
        scenario.Calls.Should().Equal(
            new List<string> { "pending.CheckCodeUnderLockAsync" },
            "the policy runs in-process after the code and before the breach check, and nothing after it was reached");
    }

    // ── The account ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheAccountIsWrittenAlreadyConfirmed()
    {
        var scenario = new Scenario();

        await scenario.RunAsync(Command());

        var user = scenario.CreatedUser!;
        user.EmailConfirmed.Should().BeTrue("the code was the proof; there is no window in which the account is recorded unconfirmed");
        user.Email.Value.Should().Be("jane@one.example", "the address comes from the pending row, never from the request");
        user.PasswordHash.Should().Be("hashed");
        user.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Doe");
        user.CreatedBy.Should().Be(Guid.Empty, "a stranger created it");
    }

    [Theory]
    [InlineData("fr", "ar", "fr")]
    [InlineData("eo", "ar", "ar")]
    [InlineData("eo", null, "en")]
    public async Task TheAccountsLanguage_IsTheCompletingRequestsCulture_ThenTheRows_ThenTheDefault(
        string requestCulture, string? rowLanguage, string expected)
    {
        // Pinned to explicit cultures rather than the runner's, so the
        // expectation cannot collapse into the production expression.
        var scenario = new Scenario(rowLanguage);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(requestCulture);
        try
        {
            await scenario.RunAsync(Command());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        scenario.CreatedUser!.PreferredLanguage.Should().Be(expected);
    }

    [Fact]
    public async Task VerifyChecksWithoutConsuming_AndCompleteConsumes()
    {
        var scenario = new Scenario();

        await scenario.RunAsync(Command());

        scenario.Users.Verify(u => u.CreateVerifiedAsync(It.IsAny<User>(), scenario.Row.Id, "123456", It.IsAny<CancellationToken>()), Times.Once,
            "the consumption happens inside the transaction that writes the account, against the row the code was checked on");
        scenario.Pending.Verify(p => p.ConsumeByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "no second consumer: the transaction did it");
    }

    [Fact]
    public async Task ACodeRejectedInsideTheTransaction_IsAnInvalidOrExpiredCode_AndNoSession()
    {
        var scenario = new Scenario().CreationAnswers(VerifiedUserCreationOutcome.CodeRejected);

        var result = await scenario.RunAsync(Command());

        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code, "the row was rotated or consumed under our feet");
        scenario.Session.Verify(s => s.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AUniqueViolationInsideTheTransaction_IsADuplicate()
    {
        var scenario = new Scenario().CreationAnswers(VerifiedUserCreationOutcome.DuplicateEmail);

        var result = await scenario.RunAsync(Command());

        result.FirstError.Code.Should().Be("User.DuplicateEmail", "another door wrote the address between the proof and the insert");
    }

    // ── After the commit, nothing fails the request ─────────────────────────

    [Fact]
    public async Task AFailureAfterCommit_StillReturnsTheSession()
    {
        var scenario = new Scenario();
        scenario.Events
            .Setup(e => e.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit store down"));
        scenario.Organization
            .Setup(o => o.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("roles unavailable"));

        var result = await scenario.RunAsync(Command(createOrganization: true));

        result.IsError.Should().BeFalse("the account exists and its row is consumed; a 500 now would be a dead end");
        result.Value.SessionId.Should().Be(scenario.SessionId);
    }

    [Fact]
    public async Task ASessionRefusalAfterCommit_IsTheOneNamedError_AndTheAccountExists()
    {
        var scenario = new Scenario();
        scenario.Session
            .Setup(s => s.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Conflict("Session.LimitReached", "too many sessions"));

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountCreatedSignInRequired",
            "one code the screen routes to sign-in on; the builder's own reason goes to the log");
        scenario.CreatedUser.Should().NotBeNull("the account was created before the session was attempted; the ordinary sign-in works");
    }

    [Fact]
    public async Task ASessionThrowAfterCommit_IsTheSameNamedError_NeverA500()
    {
        // The reachable post-commit failure: the token store throws. The
        // account exists and is confirmed; a 500 here would send the person
        // back to a code that is already spent.
        var scenario = new Scenario();
        scenario.Session
            .Setup(s => s.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refresh token store unavailable"));

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountCreatedSignInRequired");
        scenario.CreatedUser.Should().NotBeNull();
    }

    [Fact]
    public async Task TheSessionIsIssuedForTheCaller_AndEachEventIsDispatchedWhenRaised()
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeFalse();
        scenario.Session.Verify(s => s.BuildAsync(It.IsAny<User>(), "203.0.113.7", "TestAgent/1.0", "device-1", It.IsAny<CancellationToken>()), Times.Once);

        // What the aggregate carried at each dispatch, as the real dispatcher
        // would have seen it: the creation first, the sign-in second — never
        // the sign-in before the session existed, never both in one batch.
        scenario.Dispatched.Should().HaveCount(2);
        scenario.Dispatched[0].Should().Contain(typeof(UserCreatedEvent)).And.NotContain(typeof(UserLoggedInEvent));
        scenario.Dispatched[1].Should().Contain(typeof(UserLoggedInEvent)).And.NotContain(typeof(UserCreatedEvent));
    }

    [Fact]
    public async Task TheOrganization_IsOptional_AndOffByDefault()
    {
        var off = new Scenario();
        var on = new Scenario();

        await off.RunAsync(Command());
        await on.RunAsync(Command(createOrganization: true));

        off.Organization.Verify(o => o.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        on.Organization.Verify(o => o.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Fixture ─────────────────────────────────────────────────────────────

    private sealed class Scenario
    {
        public Mock<IPendingRegistrationRepository> Pending { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IAccountDeletionTombstoneRepository> Tombstones { get; } = new();
        public Mock<IPasswordHasher> Hasher { get; } = new();
        public Mock<IPasswordBreachEvaluator> Breach { get; } = new();
        public Mock<IPersonalOrganizationCreator> Organization { get; } = new();
        public Mock<IDomainEventDispatcher> Events { get; } = new();
        public Mock<ILoginResponseBuilder> Session { get; } = new();
        public RegistrationSettings Registration { get; } = new() { AllowSelfRegistration = true };
        public PendingRegistration Row { get; }
        public User? CreatedUser { get; private set; }
        public Guid SessionId { get; } = Guid.NewGuid();
        public IReadOnlyList<string> Calls => _calls;

        /// <summary>The event types the aggregate carried at each dispatch, in order.</summary>
        public List<List<Type>> Dispatched { get; } = [];

        private readonly List<string> _calls = [];
        private PendingRegistrationCodeOutcome _codeOutcome = PendingRegistrationCodeOutcome.Match;
        private VerifiedUserCreationOutcome _creationOutcome = VerifiedUserCreationOutcome.Created;
        private bool _registered;
        private bool _reserved;

        public Scenario(string? rowLanguage = "ar")
        {
            Row = PendingRegistration.Create("handle-1", "jane@one.example", Now);
            Row.TryChargeMailWindow(TimeSpan.FromSeconds(60), 3, Now);
            Row.IssueCode("hash-of-123456", 5, rowLanguage, Now);

            Pending
                .Setup(p => p.CheckCodeUnderLockAsync("handle-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("pending.CheckCodeUnderLockAsync"))
                .ReturnsAsync(() => new PendingRegistrationCodeCheck(
                    _codeOutcome,
                    _codeOutcome == PendingRegistrationCodeOutcome.NotFound ? null : Row));

            Breach
                .Setup(b => b.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("breach.EvaluateAsync"))
                .ReturnsAsync(Result.Success);

            // Narrowed to the row's address: a lookup made with anything else
            // falls through to Moq's default (false) and the refusal tests fail.
            Users
                .Setup(u => u.ExistsByEmailAsync("jane@one.example", It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("users.ExistsByEmailAsync"))
                .ReturnsAsync(() => _registered);

            Tombstones
                .Setup(t => t.ExistsByEmailHashAsync("hash:jane@one.example", It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("tombstones.ExistsByEmailHashAsync"))
                .ReturnsAsync(() => _reserved);

            Hasher
                .Setup(h => h.HashPassword(It.IsAny<string>()))
                .Callback(() => _calls.Add("hasher.HashPassword"))
                .Returns("hashed");

            Users
                .Setup(u => u.CreateVerifiedAsync(It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<User, Guid, string, CancellationToken>((user, _, _, _) =>
                {
                    _calls.Add("users.CreateVerifiedAsync");
                    CreatedUser = user;
                })
                .ReturnsAsync(() => _creationOutcome);

            Events
                .Setup(e => e.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
                .Callback<Auth.Domain.Primitives.AggregateRoot, CancellationToken>((root, _) =>
                {
                    _calls.Add("events.DispatchEventsAsync");
                    // As MediatRDomainEventDispatcher does: take what is raised, then clear it.
                    Dispatched.Add(root.DomainEvents.Select(e => e.GetType()).ToList());
                    root.ClearDomainEvents();
                })
                .Returns(Task.CompletedTask);

            Organization
                .Setup(o => o.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("organization.CreateAsync"))
                .ReturnsAsync(true);

            Session
                .Setup(s => s.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("session.BuildAsync"))
                .ReturnsAsync(new LoginResponse { SessionId = SessionId });
        }

        public Scenario CodeAnswers(PendingRegistrationCodeOutcome outcome) { _codeOutcome = outcome; return this; }
        public Scenario CreationAnswers(VerifiedUserCreationOutcome outcome) { _creationOutcome = outcome; return this; }
        public Scenario AddressRegistered() { _registered = true; return this; }
        public Scenario AddressReserved() { _reserved = true; return this; }

        public Task<ErrorOr<LoginResponse>> RunAsync(CompleteRegistrationCommand command)
        {
            var hasher = new Mock<IIdentifierHasher>();
            hasher.Setup(h => h.HashEmail(It.IsAny<string>())).Returns<string>(email => "hash:" + email);

            return new CompleteRegistrationCommandHandler(
                Pending.Object,
                Users.Object,
                new IdentifierReservationGuard(Tombstones.Object, hasher.Object),
                Hasher.Object,
                new PasswordValidator(TestHelpers.CreateOptions(TestHelpers.CreatePasswordSettings())),
                Breach.Object,
                Organization.Object,
                Events.Object,
                Session.Object,
                TestHelpers.CreateOptions(Registration),
                new Mock<ILogger<CompleteRegistrationCommandHandler>>().Object).Handle(command, CancellationToken.None);
        }
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
