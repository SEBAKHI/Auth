using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the whole-row write of the two-factor row against the one column it
/// must never touch. <c>LastUsedTimeStep</c> is written only by the conditional
/// step claim of <see cref="TwoFactorStateStore"/>, which keeps it rising; the
/// whole-row write is built from a read, so it would put back whatever step that
/// read saw — and a code accepted in between could be accepted again.
/// </summary>
public class TwoFactorAuthRepositorySqlTests
{
    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText.Replace("[", string.Empty).Replace("]", string.Empty), @"\s+", " ").Trim();

    [Fact]
    public async Task UpdateAsync_NeverWritesLastUsedTimeStep()
    {
        var protector = new Mock<ITwoFactorSecretProtector>();
        protector
            .Setup(p => p.ProtectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("v2:ciphertext");
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var twoFactor = TestHelpers.CreateTwoFactorAuth(isEnabled: true);

        await new TwoFactorAuthRepository(db, protector.Object).UpdateAsync(twoFactor, CancellationToken.None);

        var update = Sql(db.Commands.Single());
        update.Should().StartWith("UPDATE dbo.TwoFactorAuth SET", "the write under test must have been recorded");
        update.Should().NotContain("LastUsedTimeStep",
            "only the step claim may write the step, so it can only ever rise");
        db.Commands.Single().Parameters.Keys.Should().NotContain("LastUsedTimeStep");

        // The entity carries no step at all, so no whole-row write can carry one.
        typeof(TwoFactorAuth).GetProperty("LastUsedTimeStep").Should().BeNull();
    }
}
