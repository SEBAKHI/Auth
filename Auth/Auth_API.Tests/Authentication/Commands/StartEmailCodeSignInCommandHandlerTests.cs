using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.StartEmailCodeSignIn;
using Auth.Application.Features.Authentication.StartRegistration;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-215 T2 (V2): the start of a sign-in by emailed code. Every address gets the same
/// work and the same answer — the same two lookups in the same order, the same row,
/// one message, the same audit event — and only the message differs, by what the
/// address is: a sign-in code to an account (one waiting for deletion included), the
/// registration code to a free address while sign-up is open, a notice otherwise.
/// </summary>
public class StartEmailCodeSignInCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static StartEmailCodeSignInCommand Command(string email = "jane@one.example") =>
        new(email, "en", "203.0.113.7", "TestAgent/1.0");

    public enum AddressClass { Account, AccountWaitingForDeletion, FreeOpen, FreeClosed, Reserved }

    public static TheoryData<AddressClass> Classes => new()
    {
        AddressClass.Account, AddressClass.AccountWaitingForDeletion, AddressClass.FreeOpen,
        AddressClass.FreeClosed, AddressClass.Reserved,
    };

    [Fact]
    public async Task Handle_WithEmailOff_RefusesBeforeAnyLookup()
    {
        var scenario = new Scenario().As(AddressClass.Account);
        scenario.Email.Enabled = false;

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(UserErrors.EmailCodeSignInUnavailable.Code);
        scenario.Calls.Should().BeEmpty("with email off every address must get the same refusal, before it is classified");
    }

    [Theory]
    [MemberData(nameof(Classes))]
    public async Task Handle_ForEveryClass_DoesTheSameWorkAndAnswersTheSameShape(AddressClass addressClass)
    {
        var reference = new Scenario().As(AddressClass.FreeOpen);
        var scenario = new Scenario().As(addressClass);

        var expected = await reference.RunAsync(Command());
        var actual = await scenario.RunAsync(Command());

        actual.IsError.Should().BeFalse();
        scenario.Calls.Should().Equal(reference.Calls, "the work must not differ by what the address is");
        scenario.Calls.Should().Equal(
            "tombstones.ExistsByEmailHashAsync",
            "users.GetNotificationIdentityByEmailAsync",
            "pending.StartAsync",
            "notifications.SendAsync",
            "pending.MarkMailedAsync",
            "events.DispatchEventsAsync");
        JsonSerializer.Serialize(actual.Value).Should().Be(JsonSerializer.Serialize(expected.Value),
            "the answer must not differ by what the address is");
        scenario.Sent.Should().ContainSingle("one message per start");
    }

    [Theory]
    [InlineData(AddressClass.Account, NotificationTypeCodes.EmailSignInCode, true)]
    [InlineData(AddressClass.AccountWaitingForDeletion, NotificationTypeCodes.EmailSignInCode, true)]
    [InlineData(AddressClass.FreeOpen, NotificationTypeCodes.RegistrationVerification, true)]
    [InlineData(AddressClass.FreeClosed, NotificationTypeCodes.SignInNoAccount, false)]
    [InlineData(AddressClass.Reserved, NotificationTypeCodes.RegistrationAttemptExistingAccount, false)]
    public async Task Handle_SendsTheMessageOfTheAddressClass(AddressClass addressClass, string typeCode, bool carriesCode)
    {
        var scenario = new Scenario().As(addressClass);

        await scenario.RunAsync(Command());

        var message = scenario.Sent.Should().ContainSingle().Subject;
        message.TypeCode.Should().Be(typeCode);
        message.RecipientAddress.Should().Be("jane@one.example");
        message.Variables.ContainsKey("OtpCode").Should().Be(carriesCode,
            "the code goes only where it may be used; a notice's code is minted and never sent");
    }

    [Fact]
    public async Task Handle_ToAnAccountWithoutAName_GreetsItByItsAddress()
    {
        var scenario = new Scenario().As(AddressClass.Account, storedName: " ");

        await scenario.RunAsync(Command());

        var message = scenario.Sent.Single();
        message.Variables["UserName"].Should().Be("jane@one.example", "a nameless account is shown by its address (T9)");
        message.RecipientName.Should().Be("jane@one.example");
        message.RecipientUserId.Should().Be(scenario.OwnerId);
    }

    [Fact]
    public async Task Handle_WithSignUpClosed_StillSendsAnAccountItsCode()
    {
        var scenario = new Scenario().As(AddressClass.Account);
        scenario.Registration.AllowSelfRegistration = false;

        var result = await scenario.RunAsync(Command());

        result.IsError.Should().BeFalse("closing sign-up must not lock existing accounts out of the code");
        scenario.Sent.Single().TypeCode.Should().Be(NotificationTypeCodes.EmailSignInCode);
    }

    [Fact]
    public async Task Handle_WithALiveCodeAlreadyOut_SendsNothing()
    {
        var scenario = new Scenario().As(AddressClass.Account);
        scenario.StartAnswers(new PendingRegistrationStart(Scenario.Row(mailed: true), PendingRegistrationStartAction.Unchanged, null));

        await scenario.RunAsync(Command());

        scenario.Sent.Should().BeEmpty("a live code is already in the mailbox; the shared mail window bounds the rest");
        scenario.Calls.Should().NotContain("pending.MarkMailedAsync");
    }

    [Theory]
    [InlineData(AddressClass.Account, true, true)]
    [InlineData(AddressClass.FreeOpen, true, true)]
    [InlineData(AddressClass.FreeClosed, true, false)]
    [InlineData(AddressClass.Reserved, true, false)]
    [InlineData(AddressClass.Account, false, false)]
    public async Task Handle_LogsTheCode_OnlyInDevelopment_AndOnlyWhenItWasSent(
        AddressClass addressClass, bool development, bool logged)
    {
        var scenario = new Scenario().As(addressClass);
        scenario.Environment.SetupGet(e => e.IsDevelopment).Returns(development);

        await scenario.RunAsync(Command());

        scenario.CodeWasLogged().Should().Be(logged);
    }

    [Fact]
    public async Task Handle_UsesTheRegistrationsRowAndMailWindow()
    {
        var scenario = new Scenario().As(AddressClass.Account);

        await scenario.RunAsync(Command("  Jane@One.Example "));

        scenario.StartRequest.Should().NotBeNull();
        scenario.StartRequest!.Handle.Should().Be("handle:JANE@ONE.EXAMPLE", "one row per address, whichever door starts it");
        scenario.StartRequest.Email.Should().Be("jane@one.example");
        scenario.StartRequest.MaxMailsPerWindow.Should().Be(3);
        scenario.StartRequest.MailWindow.Should().Be(TimeSpan.FromSeconds(60));
    }

    private sealed class Scenario
    {
        public Mock<IPendingRegistrationRepository> Pending { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IAccountDeletionTombstoneRepository> Tombstones { get; } = new();
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IDomainEventDispatcher> Events { get; } = new();
        public Mock<IEnvironmentInfo> Environment { get; } = new();
        public Mock<ILogger<StartEmailCodeSignInCommandHandler>> Logger { get; } = new();
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
        public Guid OwnerId { get; } = Guid.NewGuid();
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
                .ReturnsAsync(() => _answer ?? new PendingRegistrationStart(Row(mailed: false), PendingRegistrationStartAction.Minted, "123456"));

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

            Environment.SetupGet(e => e.IsDevelopment).Returns(false);
        }

        public Scenario As(AddressClass addressClass, string? storedName = "Jane Doe")
        {
            _reserved = addressClass == AddressClass.Reserved;
            _owner = addressClass switch
            {
                AddressClass.Account => new UserNotificationIdentity(OwnerId, storedName, "en", IsDeleted: false),
                AddressClass.AccountWaitingForDeletion => new UserNotificationIdentity(OwnerId, storedName, "en", IsDeleted: true),
                _ => null,
            };
            Registration.AllowSelfRegistration = addressClass != AddressClass.FreeClosed;
            return this;
        }

        public void StartAnswers(PendingRegistrationStart answer) => _answer = answer;

        public Task<ErrorOr<StartRegistrationResponse>> RunAsync(StartEmailCodeSignInCommand command) =>
            new StartEmailCodeSignInCommandHandler(
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
            && invocation.Arguments[2]?.ToString()?.Contains("123456") == true);

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
