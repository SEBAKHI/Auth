using Auth.Domain.Entities;
using Auth.Domain.Errors;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// Unit tests for the <see cref="AccountDeletionVerification"/> entity.
/// Mirrors the EmailVerificationToken semantics it is modeled on.
/// </summary>
public class AccountDeletionVerificationTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private AccountDeletionVerification CreateVerification(
        DateTime? expiresAt = null,
        DateTime? usedAt = null,
        int attemptCount = 0)
    {
        return new AccountDeletionVerification(
            id: Guid.NewGuid(),
            userId: _userId,
            email: "test@example.com",
            otpHash: "argon2id_hash",
            expiresAt: expiresAt ?? DateTime.UtcNow.AddMinutes(15),
            usedAt: usedAt,
            attemptCount: attemptCount,
            createdAt: DateTime.UtcNow);
    }

    [Fact]
    public void Create_ValidParameters_SetsDefaultsAndLowercasesEmail()
    {
        var verification = AccountDeletionVerification.Create(
            _userId, "User@Example.COM", "argon2id_hash");

        verification.UserId.Should().Be(_userId);
        verification.Email.Value.Should().Be("user@example.com");
        verification.OtpHash.Should().Be("argon2id_hash");
        verification.AttemptCount.Should().Be(0);
        verification.UsedAt.Should().BeNull();
        verification.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
        verification.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_Expired_ReturnsFalse()
    {
        CreateVerification(expiresAt: DateTime.UtcNow.AddMinutes(-1))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_Used_ReturnsFalse()
    {
        CreateVerification(usedAt: DateTime.UtcNow)
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_MaxAttemptsReached_ReturnsFalse()
    {
        CreateVerification(attemptCount: AccountDeletionVerification.MaxAttempts)
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void MarkAsUsed_Unused_SetsUsedAt()
    {
        var verification = CreateVerification();

        var result = verification.MarkAsUsed();

        result.IsError.Should().BeFalse();
        verification.UsedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkAsUsed_AlreadyUsed_ReturnsGenericInvalidOtp()
    {
        var verification = CreateVerification(usedAt: DateTime.UtcNow);

        var result = verification.MarkAsUsed();

        result.FirstError.Should().Be(AccountDeletionErrors.InvalidOtp);
    }

    [Fact]
    public void IncrementAttempts_IncrementsCount()
    {
        var verification = CreateVerification();

        verification.IncrementAttempts();
        verification.IncrementAttempts();

        verification.AttemptCount.Should().Be(2);
    }
}
