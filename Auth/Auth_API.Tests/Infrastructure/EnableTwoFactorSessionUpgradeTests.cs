using System.Text.RegularExpressions;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// S08 T7, the store half: switching the factor on upgrades the session the code was
/// entered in — its row and its SSO session — INSIDE the enable transaction. The
/// factor row and the account flag must each match exactly one row (A3d); the session
/// upgrades may match none (contract A4's exception), which is logged and committed.
/// </summary>
public class EnableTwoFactorSessionUpgradeTests
{
    private const long Step = 59_313_872;
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<ILogger<TwoFactorStateStore>> _logger = new();

    private static bool Writes(RecordedCommand command, string table) =>
        Regex.IsMatch(command.CommandText, $@"^\s*UPDATE\s+\[dbo\]\.\[{table}\]");

    private static bool ReturnsTheStep(RecordedCommand command) =>
        command.CommandText.Contains("OUTPUT deleted.[LastUsedTimeStep]", StringComparison.Ordinal);

    private Task<LoginCommitOutcome> Enable(RecordingDbConnectionFactory db, Guid userId, string? idpHash) =>
        new TwoFactorStateStore(db, _logger.Object).TryEnableAsync(
            userId, "v2:ciphertext", "[]", Step, true, null,
            new SessionUpgrade(SessionId, idpHash, AuthenticationMethods.Totp), CancellationToken.None);

    private void VerifyWarning(string text, Times times) =>
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(text)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public async Task Enable_UpgradesTheSessionAndTheSsoSession_InsideItsTransaction()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null);

        (await Enable(db, userId, "idp-hash")).Should().Be(LoginCommitOutcome.Committed);

        db.Commands.Select(c => Writes(c, "TwoFactorAuth") ? "factor" : Writes(c, "Users") ? "flag"
                : Writes(c, "UserSessions") ? "session" : Writes(c, "IdpSessions") ? "sso" : c.CommandText)
            .Should().Equal("factor", "flag", "session", "sso");
        db.Commands.Should().OnlyContain(c => c.InTransaction, "the upgrades belong to the enable transaction");
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();

        var session = db.Commands.Single(c => Writes(c, "UserSessions"));
        session.Parameters["SessionId"].Should().Be(SessionId);
        session.Parameters["UserId"].Should().Be(userId);
        session.Parameters["Method"].Should().Be(AuthenticationMethods.Totp.Value);
        var sso = db.Commands.Single(c => Writes(c, "IdpSessions"));
        sso.Parameters["IdpTokenHash"].Should().Be("idp-hash");
        sso.Parameters["UserId"].Should().Be(userId);
        VerifyWarning("NotUpgraded", Times.Never());
    }

    [Fact]
    public async Task Enable_FactorRowLost_RollsBack_BeforeAnyUpgrade()
    {
        // A3d matched nothing: nothing is committed, and no session is upgraded on
        // the strength of a factor that did not switch on.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => command.CommandText.TrimStart().StartsWith("SELECT [IsEnabled]", StringComparison.Ordinal)
                ? new { IsEnabled = false }
                : null);

        (await Enable(db, Guid.NewGuid(), "idp-hash")).Should().Be(LoginCommitOutcome.FactorLost);

        db.Commands.Should().NotContain(c => Writes(c, "UserSessions") || Writes(c, "IdpSessions"));
        db.Transactions.Should().ContainSingle().Which.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_AccountFlagLost_RollsBack_BeforeAnyUpgrade()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null }
                : command.CommandText.TrimStart().StartsWith("SELECT [IsEnabled]", StringComparison.Ordinal) ? new { IsEnabled = false }
                : null,
            affectedFor: command => Writes(command, "Users") ? 0 : 1);

        (await Enable(db, Guid.NewGuid(), null)).Should().Be(LoginCommitOutcome.FactorLost);

        db.Commands.Should().NotContain(c => Writes(c, "UserSessions"));
        db.Transactions.Should().ContainSingle().Which.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_SessionUpgradesMatchNothing_LogAndStillCommit()
    {
        // The A4 exception: the session row is written on a path allowed to fail and
        // the cookie may not arrive, so an upgrade matching no row never stops the
        // factor switching on. It is logged.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null,
            affectedFor: command => Writes(command, "UserSessions") || Writes(command, "IdpSessions") ? 0 : 1);

        (await Enable(db, Guid.NewGuid(), "idp-hash")).Should().Be(LoginCommitOutcome.Committed);

        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
        VerifyWarning("TwoFactor.SessionNotUpgraded", Times.Once());
        VerifyWarning("TwoFactor.IdpSessionNotUpgraded", Times.Once());
    }

    [Fact]
    public async Task Enable_NoSsoCookie_UpgradesTheRowOnly_AndSaysWhy()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: command => ReturnsTheStep(command) ? new { LastUsedTimeStep = (long?)null } : null);

        (await Enable(db, Guid.NewGuid(), null)).Should().Be(LoginCommitOutcome.Committed);

        db.Commands.Should().Contain(c => Writes(c, "UserSessions"));
        db.Commands.Should().NotContain(c => Writes(c, "IdpSessions"));
        VerifyWarning("the request carried no SSO cookie", Times.Once());
    }
}
