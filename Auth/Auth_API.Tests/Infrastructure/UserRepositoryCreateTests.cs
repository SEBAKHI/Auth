using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// What the one INSERT into Users writes as the sign-in identifier.
///
/// It used to be the local part of the address — everything before '@' —
/// against a UNIQUE constraint on the column. Two people at different domains
/// who shared a local part collided, and because nothing caught the unique
/// violation the second registration died as a 500. Under verify-first
/// registration that would have been unrecoverable: the code had been
/// presented and the pending row was about to be consumed. The identifier is
/// now the full address, which is unique by the same rule that makes the
/// e-mail unique.
///
/// The repository is Dapper + raw SQL and the test project has no database, so
/// the parameters the repository binds are what is asserted on.
/// </summary>
public class UserRepositoryCreateTests
{
    [Fact]
    public async Task CreateAsync_WritesTheFullEmailAsUsername()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);
        await NewRepository(factory, Mock.Of<IPerUserCryptoService>())
            .CreateAsync(NewUser("Jane.Doe@Example.COM"), CancellationToken.None);

        var command = UsersInsert(factory);

        command.Parameters["Username"].Should().Be("jane.doe@example.com",
            "the address is the identifier, lower-cased by the Email value object");
        command.Parameters["Email"].Should().Be("jane.doe@example.com",
            "Username and Email are the same string now, so a collision on one is a collision on the other");
    }

    [Fact]
    public async Task CreateAsync_NeverWritesTheLocalPart()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);
        await NewRepository(factory, Mock.Of<IPerUserCryptoService>())
            .CreateAsync(NewUser("jane@one.example"), CancellationToken.None);

        var command = UsersInsert(factory);

        command.Parameters["Username"].Should().NotBe("jane",
            "jane@one.example and jane@two.example must not collide on UQ_Users_Username");
        ((string)command.Parameters["Username"]!).Should().Contain("@");
    }

    [Fact]
    public async Task CreateAsync_InsertsWithoutThePhone_ThenWritesOnlyItsCiphertext_OutsideAnyTransaction()
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);
        var user = NewUser("jane@one.example", phoneNumber: "+15551234567");
        var crypto = new Mock<IPerUserCryptoService>();
        crypto
            .Setup(c => c.EncryptAsync(user.Id, user.PhoneNumber!.Value, EncryptedFieldPurpose.UserPhoneNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v2:cipher");

        await NewRepository(factory, crypto.Object).CreateAsync(user, CancellationToken.None);

        // The ciphertext needs the per-user DEK, whose row references Users, so
        // the INSERT carries no phone at all — not even the plaintext.
        var insert = UsersInsert(factory);
        insert.Parameters["PhoneNumber"].Should().BeNull(
            "writing the plaintext into the INSERT would store an unencrypted phone number");
        insert.InTransaction.Should().BeFalse(
            "CreateAsync runs the shared insert on its own; only the verified-registration path passes a transaction");

        var update = factory.Commands.Single(c => c.CommandText.Contains("SET [PhoneNumber] = @PhoneNumber"));
        update.Parameters["PhoneNumber"].Should().Be("v2:cipher", "the phone reaches the row only as ciphertext");
        update.Parameters["Id"].Should().Be(user.Id);
    }

    [Fact]
    public void TheUsersInsert_LivesInOnePlace()
    {
        // The identifier rule above is only safe if no other creation path
        // hand-writes its own INSERT. The verified-registration path that has
        // to insert inside its own transaction is expected to reuse the shared
        // statement, and this is what fails if it does not.
        var persistence = Path.Combine(SolutionDirectory(), "Auth.Infrastructure", "Persistence");
        var insert = new Regex(@"INSERT\s+INTO\s+\[dbo\]\.\[Users\]", RegexOptions.IgnoreCase);

        var sites = Directory.GetFiles(persistence, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => insert.Matches(File.ReadAllText(file)).Select(_ => Path.GetFileName(file)))
            .ToList();

        sites.Should().Equal(["UserRepository.cs"],
            "every account is inserted by UserRepository.InsertUserAsync, so the identifier rule cannot drift between creation paths");
    }

    private static User NewUser(string email, string? phoneNumber = null) => User.Create(
        email: email,
        passwordHash: "hash",
        firstName: "Jane",
        lastName: "Doe",
        createdBy: Guid.Empty,
        phoneNumber: phoneNumber);

    private static UserRepository NewRepository(RecordingDbConnectionFactory factory, IPerUserCryptoService crypto) => new(
        factory,
        Snapshot(new PasswordSettings()),
        Mock.Of<IIdentifierHasher>(),
        Snapshot(new AccountDeletionSettings()),
        crypto);

    private static RecordedCommand UsersInsert(RecordingDbConnectionFactory factory)
    {
        var insert = factory.Commands.FirstOrDefault(c => c.CommandText.Contains("INSERT INTO [dbo].[Users]"));
        insert.Should().NotBeNull("CreateAsync must issue the Users insert");
        return insert!;
    }

    private static IOptionsSnapshot<T> Snapshot<T>(T value) where T : class
    {
        var snapshot = new Mock<IOptionsSnapshot<T>>();
        snapshot.Setup(s => s.Value).Returns(value);
        return snapshot.Object;
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
