using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Shared.Configuration;
using Auth_API.Common;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Dependent = Auth_API.Common.KeyRegenerationGuard.KeyDependent;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// X03: a key is never generated while the database still holds data written under the key it
/// would replace. Until X03 a lost secrets file on a used system was treated as a first start and
/// every key was replaced silently, the permanent identifier key included.
/// </summary>
public class KeyRegenerationGuardTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(), "authsystem-regeneration-tests", Guid.NewGuid().ToString("N"));

    public KeyRegenerationGuardTests()
    {
        Directory.CreateDirectory(_workingDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }

        GC.SuppressFinalize(this);
    }

    public static TheoryData<GeneratedSecret, string> KeysAndTheirRows => new()
    {
        { GeneratedSecret.SigningKey, "RefreshTokens" },
        { GeneratedSecret.SigningKey, "IdpSessions" },
        { GeneratedSecret.SigningKey, "ApplicationMachineKeys" },
        { GeneratedSecret.CredentialHmacKey, "RefreshTokens" },
        { GeneratedSecret.CredentialHmacKey, "IdpSessions" },
        { GeneratedSecret.CredentialHmacKey, "WebhookKeys" },
        { GeneratedSecret.CredentialHmacKey, "OrganizationInvitations" },
        { GeneratedSecret.IdentifierKey, "AccountDeletionTombstones" },
        { GeneratedSecret.PasswordPepper, "Users" },
        { GeneratedSecret.PasswordPepper, "ApiKeys" },
        { GeneratedSecret.PasswordPepper, "TwoFactorAuth" },
    };

    [Theory]
    [MemberData(nameof(KeysAndTheirRows))]
    public void EnsureSafeToGenerate_RowsWrittenUnderTheOldKey_Refuses(GeneratedSecret secret, string table)
    {
        var act = () => Check([secret], RowsIn(table));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Refusing to generate*",
                "a new key over live data orphans it with no error anywhere")
            .And.Message.Should().Contain("Restore it from backup",
                "the way out of a lost file is the file, not a new key");
    }

    [Theory]
    [InlineData(GeneratedSecret.SigningKey)]
    [InlineData(GeneratedSecret.CredentialHmacKey)]
    [InlineData(GeneratedSecret.IdentifierKey)]
    [InlineData(GeneratedSecret.PasswordPepper)]
    public void EnsureSafeToGenerate_NoRowsWrittenUnderTheOldKey_Generates(GeneratedSecret secret)
    {
        var warning = Check([secret], NoRows);

        warning.Should().BeNull("a brand-new install has nothing a new key could orphan");
    }

    [Fact]
    public void EnsureSafeToGenerate_RowsOfAnotherKeyOnly_Generates()
    {
        // The top-up mints the identifier key alone: live sessions are not its concern.
        var warning = Check(
            [GeneratedSecret.IdentifierKey], RowsIn("RefreshTokens", "IdpSessions", "WebhookKeys", "Users"));

        warning.Should().BeNull("only the keys about to be generated are checked");
    }

    [Fact]
    public void EnsureSafeToGenerate_OnlyTheGatewayToken_NeverReadsTheDatabase()
    {
        var read = false;

        var warning = KeyRegenerationGuard.EnsureSafeToGenerate(
            [GeneratedSecret.GatewayToken],
            _ =>
            {
                read = true;
                return [];
            },
            isDevelopment: false,
            overrideValue: null,
            Today);

        warning.Should().BeNull();
        read.Should().BeFalse("nothing in the database is written under the gateway token");
    }

    [Fact]
    public void Dependents_NeverCountAccountsAsSuch_SoTheSeededUsersAloneGenerate()
    {
        // The post-deployment script seeds the System user and the administrator on every new
        // database, so "the database holds users" is true on a brand-new install: a test of it
        // would refuse every first start. Users are counted only for a peppered hash.
        var userRows = KeyRegenerationGuard.Dependents.Where(dependent => dependent.Table == "Users").ToList();

        userRows.Should().ContainSingle();
        userRows[0].CountSql.Should().Contain("LIKE N'%,keyid=%'",
            "a user counts only when a password was peppered under a pepper the process cannot see");
    }

    [Fact]
    public void EnsureSafeToGenerate_InDevelopment_GeneratesAndWarnsWithWhatIsLost()
    {
        var warning = Check(
            [GeneratedSecret.CredentialHmacKey, GeneratedSecret.IdentifierKey],
            RowsIn("RefreshTokens", "AccountDeletionTombstones"),
            isDevelopment: true);

        warning.Should().NotBeNull("a fresh clone must still start");
        warning.Should().StartWith("Development:")
            .And.Contain("1 live refresh token(s)")
            .And.Contain("1 account-deletion reservation(s)")
            .And.Contain("orphaned for good");
    }

    [Fact]
    public void EnsureSafeToGenerate_TheOverrideNamingToday_GeneratesAndWarnsWithWhatIsLost()
    {
        var warning = Check(
            [GeneratedSecret.IdentifierKey], RowsIn("AccountDeletionTombstones"), overrideValue: "2026-11-02");

        warning.Should().StartWith($"{KeyRegenerationGuard.OverrideVariable}=2026-11-02:")
            .And.Contain("1 account-deletion reservation(s)");
    }

    [Theory]
    [InlineData("2026-11-01")]
    [InlineData("true")]
    [InlineData("2026-11-03")]
    public void EnsureSafeToGenerate_TheOverrideNamingAnotherDay_Refuses(string overrideValue)
    {
        var act = () => Check(
            [GeneratedSecret.IdentifierKey], RowsIn("AccountDeletionTombstones"), overrideValue: overrideValue);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("not to today's UTC date (2026-11-02), so it was ignored",
                "an override left in web.config must not stay armed");
    }

    [Fact]
    public void EnsureSafeToGenerate_APlaceholderConnectionString_RefusesNamingIt()
    {
        var act = () => KeyRegenerationGuard.EnsureSafeToGenerate(
            [GeneratedSecret.IdentifierKey], ConnectionStringGuard.Placeholder, isDevelopment: false, null, Today);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be checked*")
            .And.Message.Should().Contain("still the placeholder 'ConnectionStrings__AuthDb'");
    }

    [Fact]
    public void EnsureSafeToGenerate_AnUnreachableDatabase_RefusesNamingIt()
    {
        // Port 1 on loopback refuses at once; no retry, so the test stays fast.
        const string unreachable =
            "Server=tcp:127.0.0.1,1;Database=AuthDb;Connect Timeout=2;ConnectRetryCount=0;Encrypt=false";

        var act = () => KeyRegenerationGuard.EnsureSafeToGenerate(
            [GeneratedSecret.IdentifierKey], unreachable, isDevelopment: false, null, Today);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be checked: the database could not be reached*");
    }

    [Fact]
    public void EnsureSafeToGenerate_AMissingTable_RefusesNamingTheTablesAndTheReason()
    {
        // The real counting wrapper, over an executor that fails as SQL Server does on a schema
        // older than the build (error 208).
        var act = () => KeyRegenerationGuard.EnsureSafeToGenerate(
            [GeneratedSecret.IdentifierKey],
            dependents => KeyRegenerationGuard.CountRows(
                dependents,
                _ => throw SqlExceptions.WithNumber(208, "Invalid object name 'dbo.AccountDeletionTombstones'.")),
            isDevelopment: false,
            overrideValue: null,
            Today);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be checked: [dbo].[AccountDeletionTombstones] could not be read (Invalid object name*");
    }

    [Fact]
    public void CountRows_CountsEveryDependentInOneStatement()
    {
        // One round trip and one command timeout, so the check stays inside the host's startup
        // time limit however many keys a path generates.
        var dependents = KeyRegenerationGuard.Dependents.Take(2).ToList();
        var statements = new List<string>();

        var counts = KeyRegenerationGuard.CountRows(dependents, sql =>
        {
            statements.Add(sql);
            return [3, 0];
        });

        counts.Should().Equal(3, 0);
        statements.Should().ContainSingle().Which.Should().Be(
            $"SELECT ({dependents[0].CountSql}) AS [c0], ({dependents[1].CountSql}) AS [c1]");
    }

    [Fact]
    public void Dependents_CountOnlyTheRowsANewKeyWouldOrphan()
    {
        // Pinned: a filter turned round (Accepted for Pending, IS NOT NULL for IS NULL) would make
        // the check refuse less than it must, and CI has no database to notice. The real run on a
        // DACPAC database is what proves the filters against data.
        KeyRegenerationGuard.Dependents.ToDictionary(dependent => dependent.Table, dependent => dependent.CountSql)
            .Should().Equal(new Dictionary<string, string>
            {
                ["RefreshTokens"] = "SELECT COUNT_BIG(*) FROM [dbo].[RefreshTokens] WHERE [RevokedAt] IS NULL AND [ExpiresAt] > SYSUTCDATETIME()",
                ["IdpSessions"] = "SELECT COUNT_BIG(*) FROM [dbo].[IdpSessions] WHERE [RevokedAt] IS NULL AND [ExpiresAt] > SYSUTCDATETIME()",
                ["WebhookKeys"] = "SELECT COUNT_BIG(*) FROM [dbo].[WebhookKeys] WHERE [RevokedAt] IS NULL AND ([ExpiresAt] IS NULL OR [ExpiresAt] > SYSUTCDATETIME())",
                ["OrganizationInvitations"] = "SELECT COUNT_BIG(*) FROM [dbo].[OrganizationInvitations] WHERE [Status] = N'Pending' AND [ExpiresAt] > SYSUTCDATETIME()",
                ["ApplicationMachineKeys"] = "SELECT COUNT_BIG(*) FROM [dbo].[ApplicationMachineKeys] WHERE [RevokedAt] IS NULL",
                ["AccountDeletionTombstones"] = "SELECT COUNT_BIG(*) FROM [dbo].[AccountDeletionTombstones]",
                ["Users"] = "SELECT COUNT_BIG(*) FROM [dbo].[Users] WHERE [PasswordHash] LIKE N'%,keyid=%'",
                ["ApiKeys"] = "SELECT COUNT_BIG(*) FROM [dbo].[ApiKeys] WHERE [RevokedAt] IS NULL AND ([ExpiresAt] IS NULL OR [ExpiresAt] > SYSUTCDATETIME()) AND [KeyHash] LIKE N'%,keyid=%'",
                ["TwoFactorAuth"] = "SELECT COUNT_BIG(*) FROM [dbo].[TwoFactorAuth] WHERE [RecoveryCodes] LIKE N'%,keyid=%'",
            });
    }

    [Fact]
    public void EnsureSafeToGenerate_TheOverrideOverAnUnreadableDatabase_StillWarnsWhatALiveSystemLoses()
    {
        var warning = KeyRegenerationGuard.EnsureSafeToGenerate(
            [GeneratedSecret.IdentifierKey], ConnectionStringGuard.Placeholder, isDevelopment: false, "2026-11-02", Today);

        warning.Should().StartWith($"{KeyRegenerationGuard.OverrideVariable}=2026-11-02: generating the account-deletion identifier key without checking")
            .And.Contain("orphaned for good", "each use of the override says what is lost, read or not");
    }

    [Fact]
    public void EnsureSafeToGenerate_AnUnreadableDatabaseInDevelopment_GeneratesAndWarns()
    {
        var warning = KeyRegenerationGuard.EnsureSafeToGenerate(
            [GeneratedSecret.SigningKey], ConnectionStringGuard.Placeholder, isDevelopment: true, null, Today);

        warning.Should().StartWith("Development: generating the JWT signing key without checking the database");
    }

    [Fact]
    public void GeneratedBy_ReportsEachKeyTheGeneratorWrote()
    {
        var all = KeyRegenerationGuard.GeneratedBy(new KeyGenerationResult
        {
            RsaKeyGenerated = true,
            HmacKeyGenerated = true,
            GatewayTokenGenerated = true,
            IdentifierKeyGenerated = true
        });
        var none = KeyRegenerationGuard.GeneratedBy(new KeyGenerationResult());

        all.Should().BeEquivalentTo(
        [
            GeneratedSecret.SigningKey,
            GeneratedSecret.CredentialHmacKey,
            GeneratedSecret.GatewayToken,
            GeneratedSecret.IdentifierKey
        ]);
        none.Should().BeEmpty();
    }

    [Fact]
    public void PlainTextGenerator_ARefusedRegeneration_WritesNothing()
    {
        var target = Path.Combine(_workingDirectory, "appsettings.Staging.local.json");

        var act = () => PlainTextSecretInitializer.EnsureSecrets(
            new ConfigurationBuilder().Build(),
            _workingDirectory,
            autoGenerate: true,
            targetFile: target,
            beforeGenerate: secrets => Check(secrets, RowsIn("AccountDeletionTombstones")));

        act.Should().Throw<InvalidOperationException>();
        File.Exists(target).Should().BeFalse(
            "the check runs before the write: a refusal must not leave new keys behind for the next start");
    }

    [Fact]
    public void PlainTextGenerator_PassesExactlyTheMissingSecretsToTheCheck()
    {
        IReadOnlyCollection<GeneratedSecret>? checkedSecrets = null;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:PrivateKeyPem"] = "present",
                ["Gateway:ExpectedToken"] = "present"
            })
            .Build();

        PlainTextSecretInitializer.EnsureSecrets(
            configuration,
            _workingDirectory,
            autoGenerate: true,
            targetFile: "appsettings.Staging.local.json",
            beforeGenerate: secrets => checkedSecrets = secrets);

        checkedSecrets.Should().BeEquivalentTo(
            [GeneratedSecret.CredentialHmacKey, GeneratedSecret.IdentifierKey]);
    }

    [Fact]
    public void Dependents_ReadOnlyTablesAndColumnsTheSchemaHas()
    {
        // A typo in a table or column name would refuse every generation outside Development
        // with "could not be read", and CI has no database to notice it.
        var tables = Directory
            .EnumerateFiles(Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables"), "*.sql", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), File.ReadAllText);

        KeyRegenerationGuard.Dependents.Should().NotBeEmpty();
        foreach (var dependent in KeyRegenerationGuard.Dependents)
        {
            tables.Should().ContainKey(dependent.Table);
            dependent.CountSql.Should().Contain($"FROM [dbo].[{dependent.Table}]");

            var columns = Regex.Matches(dependent.CountSql, @"\[(\w+)\]")
                .Select(match => match.Groups[1].Value)
                .Where(name => name != "dbo" && name != dependent.Table);
            foreach (var column in columns)
            {
                tables[dependent.Table].Should().Contain($"[{column}]",
                    $"{dependent.Table} must have the column {column} the guard reads");
            }
        }
    }

    private static string? Check(
        IReadOnlyCollection<GeneratedSecret> secrets,
        Func<IReadOnlyList<Dependent>, IReadOnlyList<long>> countRows,
        bool isDevelopment = false,
        string? overrideValue = null) =>
        KeyRegenerationGuard.EnsureSafeToGenerate(secrets, countRows, isDevelopment, overrideValue, Today);

    private static IReadOnlyList<long> NoRows(IReadOnlyList<Dependent> dependents) =>
        dependents.Select(_ => 0L).ToList();

    private static Func<IReadOnlyList<Dependent>, IReadOnlyList<long>> RowsIn(params string[] tables) =>
        dependents => dependents.Select(dependent => tables.Contains(dependent.Table) ? 1L : 0L).ToList();

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
