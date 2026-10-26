using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.RemovePassword;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-215 T10 (V11): a password is removed only under the owner's six conditions
/// (D-215-3), and its removal revokes the account's other credentials, always.
/// </summary>
/// <remarks>Deliberate break: accept a session that proved only the emailed code.</remarks>
public class RemovePasswordCommandHandlerTests
{
    private static readonly AuthenticationMethods PasswordSignIn = AuthenticationMethods.Password;

    [Fact]
    public async Task Handle_WithARecentPasswordSignIn_RemovesIt_RecordsIt_AndSignsOutTheOtherDevices()
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync();

        result.IsError.Should().BeFalse();
        scenario.Users.Verify(u => u.RemovePasswordAsync(scenario.User.Id, "TestPasswordHash", scenario.User.Id, It.IsAny<CancellationToken>()), Times.Once,
            "only over the hash that was read, with its history row in the same transaction");
        scenario.User.PasswordHash.Should().BeNull();
        scenario.Revocation.Verify(r => r.RevokeCredentialsAsync(
            scenario.User.Id, scenario.SessionId, "idp-cookie", scenario.User.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "always, through the revocation service, keeping the current session");
        scenario.History.Verify(h => h.CleanupOldHistoryAsync(scenario.User.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        scenario.Dispatched.OfType<PasswordRemovedEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be(PasswordRemovalReasons.Owner);
    }

    [Fact]
    public async Task Handle_WithTheAppBound_NeedsASignInThatProvedItToo()
    {
        var refused = new Scenario().WithFactor(EmailFactorFixtures.TotpRow(Guid.Empty));
        (await refused.RunAsync()).FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);

        var accepted = new Scenario().WithFactor(EmailFactorFixtures.TotpRow(Guid.Empty)).SignedInWith(PasswordSignIn.With(AuthenticationMethods.Totp));
        (await accepted.RunAsync()).IsError.Should().BeFalse();
    }

    public static TheoryData<string, AuthenticationMethods> SessionsThatDoNotProveThePassword => new()
    {
        { "emailed code", AuthenticationMethods.EmailCode },
        { "provider", AuthenticationMethods.ExternalIdentity },
        { "unknown", AuthenticationMethods.Unknown },
    };

    [Theory]
    [MemberData(nameof(SessionsThatDoNotProveThePassword))]
    public async Task Handle_AfterASignInThatDidNotProveThePassword_AsksToSignInAgain(string signIn, AuthenticationMethods methods)
    {
        var scenario = new Scenario().SignedInWith(methods);

        var result = await scenario.RunAsync();

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code,
            $"a {signIn} sign-in does not prove the password: whoever holds only the mailbox must not turn the account code-only quietly");
        scenario.Users.Verify(u => u.RemovePasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        scenario.Revocation.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithAStaleSession_AsksToSignInAgain()
    {
        var scenario = new Scenario();
        scenario.Guard.Setup(g => g.EnsureRecentSignInAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);

        (await scenario.RunAsync()).FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
    }

    [Fact]
    public async Task Handle_WithEmailOff_IsRefused()
    {
        var scenario = new Scenario();
        scenario.Email.Enabled = false;

        (await scenario.RunAsync()).FirstError.Should().Be(UserErrors.PasswordRemovalRequiresEmail);
    }

    [Fact]
    public async Task Handle_OnAnUnconfirmedAddress_IsRefused()
    {
        var scenario = new Scenario(emailConfirmed: false);

        (await scenario.RunAsync()).FirstError.Code.Should().Be(UserErrors.EmailNotConfirmed.Code);
    }

    [Fact]
    public async Task Handle_WhileTheEmailedCodeIsTheSecondFactor_IsRefused_BeforeTheSessionIsJudged()
    {
        var scenario = new Scenario().WithFactor(EmailFactorFixtures.EmailRow(Guid.Empty));

        var result = await scenario.RunAsync();

        result.FirstError.Should().Be(UserErrors.PasswordRemovalBlockedByEmailCodeFactor,
            "after a sign-in by emailed code that factor would prove the same mailbox again");
        scenario.Guard.Invocations.Should().BeEmpty("the screen says to switch it off first, not to sign in again");
    }

    [Fact]
    public async Task Handle_WithoutAPassword_IsRefused()
    {
        var scenario = new Scenario(passwordHash: null);

        (await scenario.RunAsync()).FirstError.Should().Be(UserErrors.PasswordNotSet);
    }

    [Fact]
    public async Task Handle_WhenThePasswordChangedUnderIt_RemovesNothing_AndRevokesNothing()
    {
        var scenario = new Scenario();
        scenario.Users.Setup(u => u.RemovePasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await scenario.RunAsync();

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        scenario.Revocation.Invocations.Should().BeEmpty();
        scenario.Dispatched.Should().BeEmpty();
    }

    private sealed class Scenario
    {
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<ITwoFactorStateStore> Factors { get; } = new();
        public Mock<IReauthenticationGuard> Guard { get; } = new();
        public Mock<ICredentialRevocationService> Revocation { get; } = new();
        public Mock<IPasswordHistoryRepository> History { get; } = new();
        public Mock<IDomainEventDispatcher> Events { get; } = new();
        public EmailSettings Email { get; } = new() { Enabled = true };
        public User User { get; }
        public Guid SessionId { get; } = Guid.NewGuid();
        public List<object> Dispatched { get; } = [];

        public Scenario(bool emailConfirmed = true, string? passwordHash = "TestPasswordHash")
        {
            User = TestHelpers.CreateUser(emailConfirmed: emailConfirmed, passwordHash: passwordHash);
            Users.Setup(u => u.GetByIdAsync(User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(User);
            Users.Setup(u => u.RemovePasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            SignedInWith(PasswordSignIn);
            Events.Setup(e => e.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
                .Callback<Auth.Domain.Primitives.AggregateRoot, CancellationToken>((root, _) =>
                {
                    Dispatched.AddRange(root.DomainEvents);
                    root.ClearDomainEvents();
                })
                .Returns(Task.CompletedTask);
        }

        public Scenario SignedInWith(AuthenticationMethods methods)
        {
            Guard.Setup(g => g.EnsureRecentSignInAsync(User.Id, SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RecentSession(SessionId, "Chrome on Windows", methods));
            return this;
        }

        public Scenario WithFactor(TwoFactorSnapshot snapshot)
        {
            Factors.Setup(f => f.GetSnapshotAsync(User.Id, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            return this;
        }

        public Task<ErrorOr<Success>> RunAsync() =>
            new RemovePasswordCommandHandler(
                Users.Object, Factors.Object, Guard.Object, Revocation.Object, History.Object, Events.Object,
                TestHelpers.CreateOptions(Email), TestHelpers.CreateOptions(new PasswordSettings()),
                new Mock<ILogger<RemovePasswordCommandHandler>>().Object)
                .Handle(new RemovePasswordCommand(User.Id, SessionId, "idp-cookie"), CancellationToken.None);
    }
}
