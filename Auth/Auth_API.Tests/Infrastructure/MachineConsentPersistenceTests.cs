using Auth.Domain.Enums;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The machine directory consent survives the database round trip (OI-202, review F1). The entity
/// starts consented, so a read that dropped the column would hand every application a consent an
/// administrator switched off. Every read that builds an application is fed a row whose consent is
/// off and must answer off; the insert must write the value it was given.
/// </summary>
public class MachineConsentPersistenceTests
{
    private const string Column = "[AllowMachineOrganizationsRead]";

    /// <summary>A database whose every application row has the consent switched off.</summary>
    private static RecordingDbConnectionFactory ConsentOffDatabase() =>
        new(
            affectedRows: 0,
            rowFor: command => command.CommandText.Contains("FROM [dbo].[Applications]", StringComparison.Ordinal)
                && !command.CommandText.Contains("COUNT(", StringComparison.Ordinal)
                ? new
                {
                    Id = Guid.NewGuid(),
                    Code = "CRM",
                    Name = "CRM",
                    IsActive = true,
                    SessionTimeoutMinutes = 60,
                    MaxConcurrentSessions = 5,
                    AccessMode = (byte)1,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = Guid.NewGuid(),
                    AllowMachineOrganizationsRead = false
                }
                : null);

    public static TheoryData<string> Reads => new()
    {
        nameof(ApplicationRepository.GetByIdAsync),
        nameof(ApplicationRepository.GetByIdIncludingDeletedAsync),
        nameof(ApplicationRepository.GetByCodeAsync),
        nameof(ApplicationRepository.GetAllAsync),
        nameof(ApplicationRepository.GetActiveAsync),
        nameof(ApplicationRepository.GetPagedAsync),
    };

    [Fact]
    public void Reads_IsNotEmpty() => Reads.Count<object[]>().Should().Be(6);

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task EveryApplicationRead_SelectsTheConsent_AndHydratesItOff(string read)
    {
        var db = ConsentOffDatabase();
        var repository = new ApplicationRepository(db);

        IReadOnlyList<AppEntity?> applications = read switch
        {
            nameof(ApplicationRepository.GetByIdAsync) => new AppEntity?[] { await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None) },
            nameof(ApplicationRepository.GetByIdIncludingDeletedAsync) => new AppEntity?[] { await repository.GetByIdIncludingDeletedAsync(Guid.NewGuid(), CancellationToken.None) },
            nameof(ApplicationRepository.GetByCodeAsync) => new AppEntity?[] { await repository.GetByCodeAsync("CRM", CancellationToken.None) },
            nameof(ApplicationRepository.GetAllAsync) => (IReadOnlyList<AppEntity?>)await repository.GetAllAsync(CancellationToken.None),
            nameof(ApplicationRepository.GetActiveAsync) => (IReadOnlyList<AppEntity?>)await repository.GetActiveAsync(CancellationToken.None),
            _ => (IReadOnlyList<AppEntity?>)(await repository.GetPagedAsync(1, 20, null, null, null, SortDirection.Asc, CancellationToken.None)).Applications,
        };

        var select = db.Commands.First(command =>
            command.CommandText.Contains("FROM [dbo].[Applications]", StringComparison.Ordinal)
            && !command.CommandText.Contains("COUNT(", StringComparison.Ordinal));
        select.CommandText.Should().Contain(Column, "{0} must read the consent, or the entity's default (on) wins", read);
        applications.Should().NotBeEmpty().And.OnlyContain(application => application != null);
        applications.Should().OnlyContain(application => !application!.AllowMachineOrganizationsRead,
            "the stored value is off, and the read must not turn it back on");
        applications.Should().OnlyContain(application => application!.ConsentedMachineScopes.Count == 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_WritesTheConsent(bool allowed)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var application = AppEntity.Create("CRM", "Example CRM", null, null, Guid.NewGuid());
        application.SetMachineOrganizationsRead(allowed, Guid.NewGuid());

        await new ApplicationRepository(db).CreateAsync(application, CancellationToken.None);

        var insert = db.Commands.First(command => command.CommandText.Contains("INSERT INTO [dbo].[Applications]", StringComparison.Ordinal));
        insert.CommandText.Should().Contain(Column).And.Contain("@AllowMachineOrganizationsRead");
        insert.Parameters["AllowMachineOrganizationsRead"].Should().Be(allowed);
    }
}
