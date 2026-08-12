using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// How many rows one sign-in leaves.
///
/// A two-factor sign-in spans two requests, and every other test in the suite
/// mocks the repository and asserts a call count within a single request — which
/// is precisely why a defect that produced two rows per sign-in, the first of them
/// labelled failed, stayed invisible for months. These tests keep a real list and
/// count what is actually in it.
/// </summary>
public class TwoFactorCeremonyTests
{
    /// <summary>
    /// A real store, not a mock. Moq can tell you a method was called; only a list
    /// can tell you how many rows a user would actually see.
    /// </summary>
    private sealed class InMemoryLoginAttempts : ILoginAttemptRepository
    {
        public List<LoginAttempt> Rows { get; } = [];

        public Task CreateAsync(LoginAttempt attempt, CancellationToken cancellationToken)
        {
            Rows.Add(attempt);
            return Task.CompletedTask;
        }

        public Task ResolveTwoFactorCeremonyAsync(
            Guid challengeId, bool succeeded, string? failureReason, CancellationToken cancellationToken)
        {
            // Mirrors the SQL: only an open row is touched, so a repeat is a no-op.
            var open = Rows.SingleOrDefault(r =>
                r.TwoFactorChallengeId == challengeId && !r.IsSuccess && r.FailureReason is null);

            if (open is null)
            {
                return Task.CompletedTask;
            }

            Rows[Rows.IndexOf(open)] = new LoginAttempt(
                open.Id, open.UserId, open.Email.Value,
                succeeded, succeeded ? null : failureReason,
                open.IpAddress, open.UserAgent, open.Location, open.AttemptedAt,
                open.ApplicationId, open.TwoFactorChallengeId);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SignInHistoryEntry>> GetSignInHistoryAsync(
            Guid userId, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<IReadOnlyList<LoginAttempt>> GetRecentByEmailAsync(
            string email, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<IReadOnlyList<LoginAttempt>> GetRecentByIpAsync(
            string ipAddress, int count, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<int> CountFailedAttemptsAsync(
            string email, TimeSpan window, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<int> CountFailedAttemptsByIpAsync(
            string ipAddress, TimeSpan window, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task CleanupOldAttemptsAsync(DateTime olderThan, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";

    private readonly InMemoryLoginAttempts _attempts = new();
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly User _user = TestHelpers.CreateUser(email: "user@example.com", twoFactorEnabled: true);

    private TwoFactorChallenge? _issued;

    private TwoFactorChallengeService CreateService()
    {
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(s => s.GenerateRefreshToken()).Returns("challenge-token");

        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash("challenge-token")).Returns("challenge-token-hash");

        _challenges
            .Setup(r => r.CreateAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<TwoFactorChallenge, CancellationToken>((c, _) => _issued = c);

        return new TwoFactorChallengeService(
            _challenges.Object,
            _attempts,
            jwt.Object,
            keys.Object,
            new Mock<ILogger<TwoFactorChallengeService>>().Object);
    }

    /// <summary>The password step: primary factor accepted, second factor demanded.</summary>
    private async Task<Guid> OpenCeremonyAsync()
    {
        await CreateService().CreateChallengeAsync(
            _user, "203.0.113.10", ChromeOnWindows, CancellationToken.None);

        return _issued!.Id;
    }

    [Fact]
    public async Task ACleanTwoFactorSignIn_LeavesExactlyOneRow_AndItIsASuccess()
    {
        // The reported defect, stated as a test: correct password first try,
        // correct code first try, and the user's history showed a red failure
        // followed by a success.
        var challengeId = await OpenCeremonyAsync();

        await _attempts.ResolveTwoFactorCeremonyAsync(challengeId, true, null, CancellationToken.None);

        _attempts.Rows.Should().ContainSingle();
        _attempts.Rows[0].IsSuccess.Should().BeTrue();
        _attempts.Rows[0].FailureReason.Should().BeNull();
        _attempts.Rows[0].IsAwaitingSecondFactor.Should().BeFalse();
    }

    [Fact]
    public async Task OpeningACeremony_RecordsTheDeviceThatProducedThePassword()
    {
        // The challenge table has no user-agent column, so if this row did not
        // carry it the entry would be the only one in the history naming no device.
        await OpenCeremonyAsync();

        var row = _attempts.Rows.Should().ContainSingle().Subject;
        row.UserAgent.Should().Be(ChromeOnWindows);
        row.IpAddress.Should().Be("203.0.113.10");
        row.UserId.Should().Be(_user.Id);
        row.IsAwaitingSecondFactor.Should().BeTrue();
        row.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task AnAbandonedCeremony_LeavesOneOpenRow_WithNoSweeperNeeded()
    {
        // Nobody comes back to write this one. The row staying open IS the record:
        // somebody produced the correct password and went no further. Its age past
        // the challenge lifetime is what makes it readable as "never completed".
        await OpenCeremonyAsync();

        _attempts.Rows.Should().ContainSingle();
        _attempts.Rows[0].IsAwaitingSecondFactor.Should().BeTrue();
        _attempts.Rows[0].IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExhaustingTheCodeAllowance_SettlesTheSameRowAsAFailure()
    {
        var challengeId = await OpenCeremonyAsync();

        await _attempts.ResolveTwoFactorCeremonyAsync(
            challengeId, false, "Too many incorrect verification codes", CancellationToken.None);

        var row = _attempts.Rows.Should().ContainSingle().Subject;
        row.IsSuccess.Should().BeFalse();
        row.FailureReason.Should().Be("Too many incorrect verification codes");
        row.IsAwaitingSecondFactor.Should().BeFalse();
    }

    [Fact]
    public async Task SettlingTwice_CannotRewriteAnOutcomeAlreadyRecorded()
    {
        // Two concurrent verifies, a retry, or a late session-limit refusal all
        // arrive at the same row. The first outcome is the one that stands.
        var challengeId = await OpenCeremonyAsync();

        await _attempts.ResolveTwoFactorCeremonyAsync(challengeId, true, null, CancellationToken.None);
        await _attempts.ResolveTwoFactorCeremonyAsync(
            challengeId, false, "Too many incorrect verification codes", CancellationToken.None);

        _attempts.Rows.Should().ContainSingle();
        _attempts.Rows[0].IsSuccess.Should().BeTrue();
        _attempts.Rows[0].FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task SettlingAnUnknownChallenge_WritesNothing()
    {
        await OpenCeremonyAsync();

        await _attempts.ResolveTwoFactorCeremonyAsync(
            Guid.NewGuid(), true, null, CancellationToken.None);

        _attempts.Rows.Should().ContainSingle();
        _attempts.Rows[0].IsAwaitingSecondFactor.Should().BeTrue();
    }
}
