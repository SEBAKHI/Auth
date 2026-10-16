using System.Text.RegularExpressions;
using Auth.Infrastructure.Persistence;

namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// The statements the machine credentials rest on (OI-128, T3 and T5). The repositories are Dapper
/// with raw SQL and the test project has no database, so the SQL text is the unit under test, as in
/// ApplicationSoftDeleteSqlTests. What each pattern pins is a condition of the statement itself:
/// a read followed by a write would pass every test below if they only checked the write.
/// </summary>
public class MachineCredentialSqlTests
{
    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    // --- T3: one conditional INSERT records a jti once ---

    [Fact]
    public void ConsumeOnce_IsOneInsertConditionalOnTheJtiBeingAbsent()
    {
        var sql = ClientAssertionJtiStore.ConsumeOnce;

        Regex.Matches(sql, @"\bINSERT\b", Options).Should().ContainSingle("one statement records the use");
        sql.Should().MatchRegex(new Regex(
            @"INSERT\s+INTO\s+\[dbo\]\.\[ClientAssertionJtis\]\s*\(\[ApplicationId\],\s*\[Jti\],\s*\[ExpiresAt\]\)\s*" +
            @"SELECT\s+@ApplicationId,\s*@Jti,\s*@ExpiresAt\s+WHERE\s+NOT\s+EXISTS\s*\(", Options));
        Regex.IsMatch(sql, @"\bVALUES\b", Options).Should().BeFalse("a plain INSERT ... VALUES has no condition");
    }

    [Fact]
    public void ConsumeOnce_LocksTheKeyRangeItTests()
    {
        // Without the range lock two concurrent uses both see "absent", and the second is stopped
        // only by the primary key's error, not by the statement.
        var sql = ClientAssertionJtiStore.ConsumeOnce;

        sql.Should().MatchRegex(new Regex(
            @"NOT\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+\[dbo\]\.\[ClientAssertionJtis\]\s+WITH\s*\(\s*UPDLOCK\s*,\s*HOLDLOCK\s*\)\s+" +
            @"WHERE\s+\[ApplicationId\]\s*=\s*@ApplicationId\s+AND\s+\[Jti\]\s*=\s*@Jti\s*\)", Options));
    }

    [Fact]
    public void TheJtiTable_ScopesTheJtiToItsApplication_AndComparesItExactly()
    {
        var table = ReadDatabaseFile("ClientAssertionJtis.sql");

        table.Should().MatchRegex(new Regex(
            @"CONSTRAINT\s+\[PK_ClientAssertionJtis\]\s+PRIMARY\s+KEY\s+CLUSTERED\s+\(\[ApplicationId\],\s*\[Jti\]\)", Options));
        table.Should().MatchRegex(new Regex(@"\[Jti\]\s+NVARCHAR\(200\)\s+COLLATE\s+Latin1_General_100_BIN2\s+NOT\s+NULL", Options));
    }

    [Fact]
    public void TheJtiSweep_DeletesOnlyExpiredRows_InBatches()
    {
        var source = ReadPersistenceFile("ClientAssertionJtiStore.cs");

        source.Should().MatchRegex(new Regex(
            @"DELETE\s+TOP\s+\(@BatchSize\)\s+FROM\s+\[dbo\]\.\[ClientAssertionJtis\]\s+WHERE\s+\[ExpiresAt\]\s*<\s*@OlderThan", Options));
    }

    // --- T5: at most three active keys, counted inside the insert ---

    [Fact]
    public void InsertUnderCap_CountsTheActiveKeysUnderARangeLock_InTheSameStatement()
    {
        var sql = ApplicationMachineKeyRepository.InsertUnderCap;

        Regex.Matches(sql, @"\bINSERT\b", Options).Should().ContainSingle();
        sql.Should().MatchRegex(new Regex(
            @"INSERT\s+INTO\s+\[dbo\]\.\[ApplicationMachineKeys\].*SELECT\s+@Id,.*WHERE\s*\(\s*" +
            @"SELECT\s+COUNT\(\*\)\s+FROM\s+\[dbo\]\.\[ApplicationMachineKeys\]\s+WITH\s*\(\s*UPDLOCK\s*,\s*HOLDLOCK\s*\)\s+" +
            // OI-171: the cap counts the keys of the new key's purpose, under the same locks.
            @"WHERE\s+\[ApplicationId\]\s*=\s*@ApplicationId\s+AND\s+\[RevokedAt\]\s+IS\s+NULL\s+AND\s+\[Purpose\]\s*=\s*@Purpose\s*\)\s*<\s*@MaxActiveKeys", Options));
    }

    // --- T2: a key authenticates only while it and its application are alive ---

    [Fact]
    public void KeyLookupByKid_JoinsTheLiveApplication_AndSkipsRevokedKeys()
    {
        var source = ReadPersistenceFile("ApplicationMachineKeyRepository.cs");
        var lookup = source[source.IndexOf("GetActiveByKidAsync(string kid", StringComparison.Ordinal)..];
        lookup = lookup[..lookup.IndexOf("RecordUsageAsync", StringComparison.Ordinal)];

        lookup.Should().MatchRegex(new Regex(@"INNER\s+JOIN\s+\[dbo\]\.\[Applications\]\s+a\s+ON\s+a\.\[Id\]\s*=\s*k\.\[ApplicationId\]", Options));
        lookup.Should().MatchRegex(new Regex(@"WHERE\s+k\.\[Kid\]\s*=\s*@Kid\s+AND\s+k\.\[RevokedAt\]\s+IS\s+NULL\s+AND\s+a\.\[IsActive\]\s*=\s*1\s+AND\s+a\.\[IsDeleted\]\s*=\s*0", Options));
    }

    [Fact]
    public void KeyReadsForTheConsole_AreScopedToTheApplicationInTheRoute()
    {
        var source = ReadPersistenceFile("ApplicationMachineKeyRepository.cs");

        source.Should().MatchRegex(new Regex(@"WHERE\s+k\.\[Id\]\s*=\s*@Id\s+AND\s+k\.\[ApplicationId\]\s*=\s*@ApplicationId", Options),
            "a key of another application must read as no key");
        source.Should().MatchRegex(new Regex(
            @"UPDATE\s+\[dbo\]\.\[ApplicationMachineKeys\]\s+SET.*WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[ApplicationId\]\s*=\s*@ApplicationId\s+AND\s+\[RevokedAt\]\s+IS\s+NULL", Options),
            "a revocation is conditional on the key still being active");
    }

    [Fact]
    public void TheKeyTable_HasAUniqueKid_ComparedExactly()
    {
        var table = ReadDatabaseFile("ApplicationMachineKeys.sql");

        table.Should().MatchRegex(new Regex(@"CREATE\s+UNIQUE\s+NONCLUSTERED\s+INDEX\s+\[UX_ApplicationMachineKeys_Kid\]\s+ON\s+\[dbo\]\.\[ApplicationMachineKeys\]\s+\(\[Kid\]\)", Options));
        table.Should().MatchRegex(new Regex(@"\[Kid\]\s+NVARCHAR\(64\)\s+COLLATE\s+Latin1_General_100_BIN2\s+NOT\s+NULL", Options));
        Regex.IsMatch(table, @"\[\w*Private\w*\]", RegexOptions.IgnoreCase).Should().BeFalse("the table holds no private key column");
    }

    private static string ReadPersistenceFile(string fileName) =>
        File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth.Infrastructure", "Persistence", fileName));

    private static string ReadDatabaseFile(string fileName) =>
        File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", fileName));

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
