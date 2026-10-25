using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S24 PR B, OI-220 (2) (a). A setup may write an authenticator secret onto a row whose
/// factor is the emailed code (setup is how the app that will replace it begins), and
/// it is kept there until the replacement commits. While the emailed code is the
/// factor, no authenticator code verifies anywhere: not at sign-in, not at step-up, not
/// to switch a factor off, renew recovery codes or replace an authenticator. The only
/// door that checks it is binding the app itself, which also needs the replacement
/// proof (EmailCodeLifecycleCommandHandlerTests). The recovery leg is in
/// AccountDeletionRecovererEmailTests.
/// </summary>
public class EmailCodeRowAuthenticatorCodeTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();

    public EmailCodeRowAuthenticatorCodeTests()
    {
        _store.Setup(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailRow(_userId, recoveryCodes: "[\"h1\"]", protectedSecretKey: "v2:pending-app-secret"));
        _store.Setup(s => s.TryReserveAttemptAsync(_userId, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _protector.Setup(p => p.UnprotectAsync(_userId, "v2:pending-app-secret", It.IsAny<CancellationToken>())).ReturnsAsync("PLAIN");
        _totp.Setup(t => t.ValidateCode("PLAIN", It.IsAny<string>())).Returns(59_313_872);
    }

    private SecondFactorVerifier Verifier() => new(
        _store.Object,
        [
            new TotpProofStrategy(_totp.Object, _protector.Object),
            new RecoveryCodeProofStrategy(_totp.Object),
            new EmailCodeProofStrategy(ScopedOtpHasher.Instance)
        ],
        TestHelpers.CreateSecondFactorLockout());

    [Fact]
    public async Task AtSignIn_AnAuthenticatorCode_IsRefusedUncounted_AndNeverChecked()
    {
        var challenge = ChallengeWith(_userId, AuthenticationMethods.Password);

        var result = await Verifier().ReserveAsync(
            new SecondFactorPresentation(_userId, SecondFactorMethod.Totp, "123456", challenge), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _protector.Verify(p => p.UnprotectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnEveryPathThatExpectsAnEnabledAuthenticator_TheRowIsNotOne()
    {
        // Step-up, switching the app off, new recovery codes and a new authenticator
        // all reserve with expectEnabled: true.
        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.TwoFactorNotEnabled);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheAuthenticatorStrategy_HoldsOnlyAnEnabledRow()
    {
        new TotpProofStrategy(_totp.Object, _protector.Object)
            .IsBound(EmailRow(_userId, protectedSecretKey: "v2:pending-app-secret"))
            .Should().BeFalse("a secret on an emailed-code row is a setup in progress, not a bound app");
    }
}
