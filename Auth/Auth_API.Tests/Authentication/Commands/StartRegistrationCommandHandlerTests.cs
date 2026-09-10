using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.StartRegistration;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The first step of verify-first registration. Two rules govern it: nothing
/// is created for an address before its code comes back, and every address —
/// free, taken, soft-deleted, reserved — gets the same work and the same
/// answer, differing only in which message goes out and to whom.
/// </summary>
public class StartRegistrationCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static StartRegistrationCommand Command(string email = "jane@one.example", string? language = "en") =>
        new(email, language, "203.0.113.7", "TestAgent/1.0");

    // ── The door and the invariant ───────────────────────────────────────────

    [Fact]
    public async Task ClosedServer_RefusesBeforeTouchingTheAddress()
    {
        var scenario = new Scenario().Existing();
        scenario.Registration.AllowSelfRegistration = false;

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.SelfRegistrationClosed");
        scenario.Calls.Should().BeEmpty(
            "when the door is shut every address must get the same refusal; a lookup before it would make the door an oracle");
        scenario.Tombstones.VerifyNoOtherCalls();
        scenario.Users.VerifyNoOtherCalls();
        scenario.Pending.VerifyNoOtherCalls();
        scenario.Notifications.VerifyNoOtherCalls();
        scenario.Events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClosedServer_AnswersATakenAddressIdenticallyToAFreeOne()
    {
        var free = new Scenario().Free();
        var taken = new Scenario().Existing();
        free.Registration.AllowSelfRegistration = false;
        taken.Registration.AllowSelfRegistration = false;

        var forFree = await free.RunAsync(Command());
        var forTaken = await taken.RunAsync(Command());

        forFree.FirstError.Should().Be(forTaken.FirstError);
    }

    [Fact]
    public async Task NothingIsCreated()
    {
        // By construction and by behaviour. The handler cannot hash a password
        // or create an organization because nothing hands it the means; and a
        // start for a free address never writes a user.
        var dependencies = typeof(StartRegistrationCommandHandler)
            .GetConstructors().Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToList();

        dependencies.Should().NotContain("IPasswordHasher");
        dependencies.Should().NotContain("IPersonalOrganizationCreator");
        dependencies.Should().NotContain("PasswordValidator");
        dependencies.Should().NotContain("IPasswordBreachEvaluator");

        var scenario = new Scenario().Free();
        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeFalse();

        // Closed, not enumerated: the light identity read is the ONLY call a
        // start may make on the user repository, whatever writes the interface
        // gains later (the completion step adds one), and the tombstone check
        // is the only call on the tombstone repository.
        scenario.Users.Verify(u => u.GetNotificationIdentityByEmailAsync("jane@one.example", It.IsAny<CancellationToken>()), Times.Once);
        scenario.Users.VerifyNoOtherCalls();
        scenario.Tombstones.Verify(t => t.ExistsByEmailHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        scenario.Tombstones.VerifyNoOtherCalls();
    }

    // ── One answer, one amount of work, for every address ───────────────────

    [Fact]
    public async Task ExistingAccount_AnswersExactlyAsAFreeAddressDoes()
    {
        var scenarios = new Dictionary<string, Scenario>
        {
            ["free"] = new Scenario().Free(),
            ["existing"] = new Scenario().Existing(),
            ["soft-deleted"] = new Scenario().SoftDeleted(),
            ["reserved"] = new Scenario().Reserved(),
        };

        var bodies = new Dictionary<string, string>();
        var calls = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (name, scenario) in scenarios)
        {
            var result = await scenario.RunAsync(Command(" Jane@One.Example "));
            result.IsError.Should().BeFalse($"the {name} address must be acknowledged like every other");
            bodies[name] = JsonSerializer.Serialize(result.Value);
            calls[name] = scenario.Calls;
        }

        bodies.Values.Distinct().Should().ContainSingle(
            "the response body must be byte-identical for a free, taken, soft-deleted and reserved address");
        calls.Values.Select(sequence => string.Join(" > ", sequence)).Distinct().Should().ContainSingle(
            "the same calls in the same order: a branch that skips a lookup answers faster, and faster is an answer");
        calls["free"].Should().Equal(
            "tombstones.ExistsByEmailHashAsync",
            "users.GetNotificationIdentityByEmailAsync",
            "pending.StartAsync",
            "notifications.SendAsync",
            "pending.MarkMailedAsync",
            "events.DispatchEventsAsync");
    }

    [Fact]
    public async Task Start_SendsTheCodeOnlyToAFreeAddress_AndTheNoticeToEveryOther()
    {
        var free = new Scenario().Free();
        var existing = new Scenario().Existing();
        var softDeleted = new Scenario().SoftDeleted();
        var reserved = new Scenario().Reserved();

        await free.RunAsync(Command());
        await existing.RunAsync(Command());
        await softDeleted.RunAsync(Command());
        await reserved.RunAsync(Command());

        var code = free.Sent.Single();
        code.TypeCode.Should().Be(NotificationTypeCodes.RegistrationVerification);
        code.RecipientAddress.Should().Be("jane@one.example");
        code.Variables["OtpCode"].Should().Be("123456");
        code.Variables["ExpirationMinutes"].Should().Be(5);

        foreach (var (name, scenario) in new[] { ("existing", existing), ("soft-deleted", softDeleted), ("reserved", reserved) })
        {
            var notice = scenario.Sent.Single();
            notice.TypeCode.Should().Be(NotificationTypeCodes.RegistrationAttemptExistingAccount,
                $"the {name} address gets the notice, never a code");
            notice.RecipientAddress.Should().Be("jane@one.example");
            notice.Variables.Keys.Should().BeEquivalentTo(["SignInLink", "ResetPasswordLink", "AttemptedAt"]);
            notice.Variables["SignInLink"].Should().Be("https://accounts.example/login");
            notice.Variables["ResetPasswordLink"].Should().Be("https://accounts.example/forgot-password");
            notice.Variables.Should().NotContainKey("OtpCode", "the code minted for a taken address is never sent");
        }
    }

    [Fact]
    public async Task AnAddressInDeletionGrace_IsClassifiedExisting_AtStart()
    {
        // A Users row in any state blocks the address — the unique index does
        // not care about status — so the owner is told, in their language.
        var scenario = new Scenario().SoftDeleted(language: "fr");

        await scenario.RunAsync(Command(language: "en"));

        scenario.Sent.Single().TypeCode.Should().Be(NotificationTypeCodes.RegistrationAttemptExistingAccount);
        scenario.Sent.Single().LanguageCode.Should().Be("fr");
    }

    [Fact]
    public async Task TheNoticeToAnExistingOwner_UsesTheOwnersLanguage_AndIsAnonymous()
    {
        var existing = new Scenario().Existing(language: "ar");
        var reserved = new Scenario().Reserved();

        await existing.RunAsync(Command(language: "en"));
        await reserved.RunAsync(Command(language: "tr"));

        var toOwner = existing.Sent.Single();
        toOwner.LanguageCode.Should().Be("ar", "the owner reads their own language, not the stranger's");
        toOwner.RecipientUserId.Should().Be(existing.OwnerId);
        toOwner.TriggeredBy.Should().Be(Guid.Empty, "the actor is an anonymous stranger");

        var toReserved = reserved.Sent.Single();
        toReserved.LanguageCode.Should().Be("tr", "a reserved address has no owner on file; the request language is all there is");
        toReserved.RecipientUserId.Should().BeNull();
        toReserved.TriggeredBy.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task TheCodeMessage_UsesTheRequestLanguage_AndNamesNobody()
    {
        var scenario = new Scenario().Free();

        await scenario.RunAsync(Command(language: "AR-sa"));

        var code = scenario.Sent.Single();
        code.LanguageCode.Should().Be("ar", "stated explicitly so the renderer looks nobody up");
        code.RecipientUserId.Should().BeNull("there is no user");
        code.RecipientName.Should().BeNull("there is no name");
        code.TriggeredBy.Should().Be(Guid.Empty);
        scenario.StartRequest!.PreferredLanguage.Should().Be("ar", "the row stores the language the code went out in");
    }

    // ── The row ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_AsksTheRepositoryForTheNormalizedAddress_UnderItsHandle_WithTheMailWindow()
    {
        var scenario = new Scenario().Free();

        await scenario.RunAsync(Command(" Jane@One.Example "));

        var request = scenario.StartRequest!;
        request.Email.Should().Be("jane@one.example");
        request.Handle.Should().Be("handle:JANE@ONE.EXAMPLE", "the handle is derived from the normalized key, so the same address always gets the same handle");
        request.ExpirationMinutes.Should().Be(5);
        request.MailWindow.Should().Be(TimeSpan.FromSeconds(60));
        request.MaxMailsPerWindow.Should().Be(3);
    }

    [Fact]
    public async Task WhenTheRepositoryLeavesALiveCodeAlone_StartSendsNothing_AndReturnsTheSameHandleAndExpiry()
    {
        // The rule itself — a live, mailed code is neither rotated nor
        // invalidated — lives under the row lock and is proved by
        // PendingRegistrationRepositoryTests.Start_OnALiveMailedCode_LeavesItAlone.
        // This is the handler's half: given Unchanged, it mails nothing, stamps
        // nothing, and answers the same handle and the stored expiry.
        var scenario = new Scenario().Free();
        var row = Scenario.Row(mailed: true);
        scenario.StartAnswers(new PendingRegistrationStart(row, PendingRegistrationStartAction.Unchanged, Code: null));

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeFalse();
        result.Value.PendingId.Should().Be(row.Handle);
        result.Value.ExpiresAt.Should().Be(row.ExpiresAt);
        scenario.Sent.Should().BeEmpty("the code is in someone's inbox already");
        scenario.Pending.Verify(p => p.MarkMailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheExpiryReturned_IsTheRowsExpiry_InEveryBranch()
    {
        // The row's code was issued three minutes ago with five to live: the
        // stored expiry is two minutes out, the nominal one would be five.
        var minted = new Scenario().Free();
        var unchanged = new Scenario().Free();
        var taken = new Scenario().Existing();
        unchanged.StartAnswers(new PendingRegistrationStart(Scenario.Row(mailed: true), PendingRegistrationStartAction.Unchanged, Code: null));

        var forMinted = await minted.RunAsync(Command());
        var forUnchanged = await unchanged.RunAsync(Command());
        var forTaken = await taken.RunAsync(Command());

        foreach (var response in new[] { forMinted, forUnchanged, forTaken })
        {
            response.Value.ExpiresAt.Should().Be(Now.AddMinutes(2), "the stored expiry, never now + OtpExpirationMinutes");
        }
    }

    // ── The message and the stamp ───────────────────────────────────────────

    [Fact]
    public async Task ARenderOrEnqueueFailure_DoesNotChangeTheStartResponse()
    {
        var healthy = new Scenario().Free();
        var failing = new Scenario().Free();
        failing.Notifications
            .Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Notification.TemplateNotPublished", "no template"));

        var expected = await healthy.RunAsync(Command());
        var actual = await failing.RunAsync(Command());

        actual.IsError.Should().BeFalse();
        JsonSerializer.Serialize(actual.Value).Should().Be(JsonSerializer.Serialize(expected.Value));
        failing.Pending.Verify(p => p.MarkMailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "MailedAt stays null so the next start mints again instead of trusting a message nobody got");
    }

    [Fact]
    public async Task TheRowIsStampedMailed_OnlyAfterTheEnqueueSucceeds()
    {
        var scenario = new Scenario().Free();

        await scenario.RunAsync(Command());

        var row = scenario.StartedRow!;
        scenario.Pending.Verify(p => p.MarkMailedAsync(row.Id, row.OtpHash, It.IsAny<CancellationToken>()), Times.Once,
            "the stamp names the code that was mailed, so a rotation in between is not stamped by mistake");
        var calls = scenario.Calls.ToList();
        calls.IndexOf("notifications.SendAsync").Should().BeLessThan(calls.IndexOf("pending.MarkMailedAsync"));
    }

    [Fact]
    public async Task MaskedEmail_IsComputedFromTheNormalizedInput()
    {
        var free = new Scenario().Free();
        var existing = new Scenario().Existing();

        var forFree = await free.RunAsync(Command(" Jane.Doe@Example.COM "));
        var forExisting = await existing.RunAsync(Command(" Jane.Doe@Example.COM "));

        forFree.Value.MaskedEmail.Should().Be("j****e@example.com", "trimmed and lower-cased before masking");
        forExisting.Value.MaskedEmail.Should().Be(forFree.Value.MaskedEmail, "the stored address must never leak its own casing into the reply");
    }

    // ── The development log line ────────────────────────────────────────────

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public async Task TheCodeIsLoggedOnlyInDevelopmentWithEmailDisabled(bool emailEnabled, bool isDevelopment, bool logged)
    {
        var scenario = new Scenario().Free();
        scenario.Email.Enabled = emailEnabled;
        scenario.Environment.Setup(e => e.IsDevelopment).Returns(isDevelopment);

        await scenario.RunAsync(Command());

        scenario.CodeWasLogged().Should().Be(logged,
            "the code is the whole proof of the address, and Email:Enabled is a hot setting an operator can flip in production");
    }

    [Fact]
    public async Task ACodeMintedForATakenAddress_IsNeverLogged_EvenInDevelopment()
    {
        var scenario = new Scenario().Existing();
        scenario.Email.Enabled = false;
        scenario.Environment.Setup(e => e.IsDevelopment).Returns(true);

        await scenario.RunAsync(Command());

        scenario.CodeWasLogged().Should().BeFalse("a code that never goes out must not exist in the log either");
    }

    // ── The audit trail ─────────────────────────────────────────────────────

    [Fact]
    public async Task StartRaisesTheAuditEvent_OnTheRow_WithIpAndUserAgent_AndNoUser()
    {
        var scenario = new Scenario().Free();

        await scenario.RunAsync(Command());

        var row = scenario.StartedRow!;
        scenario.Events.Verify(e => e.DispatchEventsAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        var raised = row.DomainEvents.OfType<RegistrationStartedEvent>().Single();
        raised.PendingRegistrationId.Should().Be(row.Id);
        raised.Email.Should().Be("jane@one.example");
        raised.IpAddress.Should().Be("203.0.113.7");
        raised.UserAgent.Should().Be("TestAgent/1.0");
    }

    [Fact]
    public async Task TheAuditEvent_IsRaisedForATakenAddressToo()
    {
        var scenario = new Scenario().Existing();

        await scenario.RunAsync(Command());

        scenario.StartedRow!.DomainEvents.OfType<RegistrationStartedEvent>().Should().ContainSingle(
            "abusive attempts against existing addresses are exactly what gets monitored");
    }

    [Fact]
    public async Task AnAuditFailure_DoesNotChangeTheStartResponse()
    {
        var healthy = new Scenario().Free();
        var failing = new Scenario().Free();
        failing.Events
            .Setup(e => e.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit store down"));

        var expected = await healthy.RunAsync(Command());
        var actual = await failing.RunAsync(Command());

        actual.IsError.Should().BeFalse("the row is committed and the message sent; a 500 now would be a dead end");
        JsonSerializer.Serialize(actual.Value).Should().Be(JsonSerializer.Serialize(expected.Value),
            "a different body after an audit failure would tell the caller which starts were not audited");
        failing.Pending.Verify(p => p.MarkMailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "the audit sits after the stamp; a failure there undoes nothing before it");
    }

    // ── Fixture ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One handler with its collaborators mocked, plus the address's class.
    /// Every scenario answers the repository with the same deterministic row so
    /// responses across classes can be compared byte for byte.
    /// </summary>
    private sealed class Scenario
    {
        public Mock<IPendingRegistrationRepository> Pending { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IAccountDeletionTombstoneRepository> Tombstones { get; } = new();
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IDomainEventDispatcher> Events { get; } = new();
        public Mock<IEnvironmentInfo> Environment { get; } = new();
        public Mock<ILogger<StartRegistrationCommandHandler>> Logger { get; } = new();
        public RegistrationSettings Registration { get; } = new() { AllowSelfRegistration = true };
        public EmailSettings Email { get; } = new()
        {
            OtpExpirationMinutes = 5,
            MaxOtpRequestsPerWindow = 3,
            RateLimitWindowSeconds = 60,
            Enabled = true,
            FrontendBaseUrl = "https://accounts.example/"
        };

        public List<NotificationRequest> Sent { get; } = [];
        public PendingRegistrationStartRequest? StartRequest { get; private set; }
        public PendingRegistration? StartedRow { get; private set; }
        public Guid OwnerId { get; } = Guid.NewGuid();

        /// <summary>Every collaborator call, in the order it was made, across all mocks.</summary>
        public IReadOnlyList<string> Calls => _calls;

        private readonly List<string> _calls = [];
        private readonly IPendingRegistrationHandle _handle;
        private readonly IIdentifierHasher _hasher;
        private PendingRegistrationStart? _answer;
        private bool _reserved;
        private UserNotificationIdentity? _owner;

        public Scenario()
        {
            var handle = new Mock<IPendingRegistrationHandle>();
            handle.Setup(h => h.For(It.IsAny<string>())).Returns<string>(key => "handle:" + key);
            _handle = handle.Object;

            var hasher = new Mock<IIdentifierHasher>();
            hasher.Setup(h => h.HashEmail(It.IsAny<string>())).Returns<string>(email => "hash:" + email);
            _hasher = hasher.Object;

            // Moq numbers invocations per mock, not across mocks, so the global
            // order the enumeration tests compare is recorded by the callbacks.
            Tombstones
                .Setup(t => t.ExistsByEmailHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("tombstones.ExistsByEmailHashAsync"))
                .ReturnsAsync(() => _reserved);

            Users
                .Setup(u => u.GetNotificationIdentityByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("users.GetNotificationIdentityByEmailAsync"))
                .ReturnsAsync(() => _owner);

            Pending
                .Setup(p => p.StartAsync(It.IsAny<PendingRegistrationStartRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PendingRegistrationStartRequest, CancellationToken>((request, _) =>
                {
                    _calls.Add("pending.StartAsync");
                    StartRequest = request;
                })
                .ReturnsAsync(() =>
                {
                    var answer = _answer ?? new PendingRegistrationStart(Row(mailed: false), PendingRegistrationStartAction.Minted, "123456");
                    StartedRow = answer.Row;
                    return answer;
                });

            Pending
                .Setup(p => p.MarkMailedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("pending.MarkMailedAsync"))
                .Returns(Task.CompletedTask);

            Notifications
                .Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<NotificationRequest, CancellationToken>((request, _) =>
                {
                    _calls.Add("notifications.SendAsync");
                    Sent.Add(request);
                })
                .ReturnsAsync(Result.Success);

            Events
                .Setup(e => e.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
                .Callback(() => _calls.Add("events.DispatchEventsAsync"))
                .Returns(Task.CompletedTask);
        }

        public Scenario Free()
        {
            _reserved = false;
            _owner = null;
            return this;
        }

        public Scenario Existing(string language = "en")
        {
            _reserved = false;
            _owner = new UserNotificationIdentity(OwnerId, "Jane Doe", language, IsDeleted: false);
            return this;
        }

        public Scenario SoftDeleted(string language = "en")
        {
            _reserved = false;
            _owner = new UserNotificationIdentity(OwnerId, "Jane Doe", language, IsDeleted: true);
            return this;
        }

        public Scenario Reserved()
        {
            _reserved = true;
            _owner = null;
            return this;
        }

        public void StartAnswers(PendingRegistrationStart answer) => _answer = answer;

        public Task<ErrorOr<StartRegistrationResponse>> RunAsync(StartRegistrationCommand command) =>
            new StartRegistrationCommandHandler(
                Pending.Object,
                _handle,
                Users.Object,
                new IdentifierReservationGuard(Tombstones.Object, _hasher),
                Notifications.Object,
                Events.Object,
                TestHelpers.CreateOptions(Registration),
                TestHelpers.CreateOptions(Email),
                Environment.Object,
                Logger.Object).Handle(command, CancellationToken.None);

        public bool CodeWasLogged() => Logger.Invocations.Any(invocation =>
            invocation.Method.Name == nameof(ILogger.Log)
            && invocation.Arguments[0] is LogLevel.Warning
            && invocation.Arguments[2]?.ToString()?.Contains("123456") == true);

        /// <summary>
        /// A row whose code was issued three minutes ago with five to live, so
        /// its stored expiry (two minutes out) differs from any nominal one.
        /// </summary>
        public static PendingRegistration Row(bool mailed)
        {
            var issued = Now.AddMinutes(-3);
            var row = PendingRegistration.Create("handle:JANE@ONE.EXAMPLE", "jane@one.example", issued);
            row.TryChargeMailWindow(TimeSpan.FromSeconds(60), 3, issued);
            row.IssueCode("hash-of-123456", 5, "en", issued);
            if (mailed) row.MarkMailed(issued);
            return row;
        }
    }
}
