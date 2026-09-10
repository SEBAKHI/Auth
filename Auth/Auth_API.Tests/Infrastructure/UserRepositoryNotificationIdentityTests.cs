using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The light identity read a registration start classifies an address with.
/// It runs for every start, free address or taken, so it has to cost the same
/// for both: three columns, no soft-delete filter, no phone decryption.
/// </summary>
public class UserRepositoryNotificationIdentityTests
{
    [Fact]
    public async Task ReadsThreeColumns_ByTheNormalizedAddress_IncludingSoftDeletedRows()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 0);

        var identity = await NewRepository(factory)
            .GetNotificationIdentityByEmailAsync(" Jane.Doe@Example.COM ", CancellationToken.None);

        identity.Should().BeNull("the double answered no row");
        var read = factory.Commands.Single();
        read.CommandText.Should().Contain("FROM [dbo].[Users]");
        read.CommandText.Should().Contain("[NormalizedEmail] = @NormalizedEmail");
        read.Parameters["NormalizedEmail"].Should().Be("JANE.DOE@EXAMPLE.COM");

        read.CommandText.Should().NotContain("IsDeleted] = 0",
            "a soft-deleted row still blocks the address, so for a registration attempt it is 'existing'");
        read.CommandText.Should().NotContain("PhoneNumber",
            "nothing here needs the phone, and decrypting it would cost a taken address more than a free one");
        read.CommandText.Should().NotContain("PasswordHash");
    }

    [Fact]
    public async Task MapsTheRow_WithItsDeletionFlag()
    {
        var id = Guid.NewGuid();
        var factory = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new { Id = id, DisplayName = (string?)"Jane Doe", PreferredLanguage = "ar", IsDeleted = true });

        var identity = await NewRepository(factory)
            .GetNotificationIdentityByEmailAsync("jane.doe@example.com", CancellationToken.None);

        identity.Should().NotBeNull();
        identity!.Id.Should().Be(id);
        identity.DisplayName.Should().Be("Jane Doe");
        identity.PreferredLanguage.Should().Be("ar");
        identity.IsDeleted.Should().BeTrue();
    }

    private static UserRepository NewRepository(RecordingDbConnectionFactory factory) => new(
        factory,
        TestHelpers.CreateOptions(new PasswordSettings()),
        Mock.Of<IIdentifierHasher>(),
        TestHelpers.CreateOptions(new AccountDeletionSettings()),
        Mock.Of<IPerUserCryptoService>(),
        Mock.Of<IOtpHasher>());
}
