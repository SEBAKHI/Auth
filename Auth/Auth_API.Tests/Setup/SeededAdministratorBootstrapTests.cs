using System.Text.RegularExpressions;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Infrastructure;
using Auth_Setup;

namespace Auth_API.Tests.Setup;

/// <summary>
/// OI-50: Auth_Setup gives the seeded administrator the operator's real address,
/// marked confirmed, together with the password — keyed on the seeded Id, never on
/// the placeholder address the seed ships with (T1–T8 of the follow-up's notes).
/// </summary>
public class SeededAdministratorBootstrapTests
{
    private const string Hash = "$argon2id$v=19$m=65536,t=3,p=1$c2FsdA$aGFzaA";

    private static string Build(string address) =>
        SeededAdministratorBootstrap.Build(SeededAdministratorBootstrap.ValidateAddress(address).Value, Hash);

    private static string Root => ApiSourceScan.SolutionDirectory();

    // ── T1: keyed on the seeded Id and on the row not being deleted ───────

    [Fact]
    public void T1_TheStatement_IsKeyedOnTheSeededIdAndALiveRow()
    {
        var sql = Build("operator@example.org");

        sql.Should().Contain("WHERE [Id] = '00000000-0000-0000-0000-000000000002'\n  AND [IsDeleted] = 0;");
        sql.Should().NotContain("WHERE [Email]", "the statement SETS the address; selecting by it would match nothing after the first run");
    }

    // ── T2: exactly the five columns, normalized as the lookup normalizes ─

    [Fact]
    public void T2_TheStatement_AssignsExactlyTheFiveColumns()
    {
        const string typed = "  Operator.Name@Example.ORG ";
        var email = Email.Create(typed).Value;

        var sql = Build(typed);

        var setList = sql[sql.IndexOf("SET ", StringComparison.Ordinal)..sql.IndexOf("WHERE", StringComparison.Ordinal)];
        Regex.Matches(setList, @"\[(\w+)\] =").Select(m => m.Groups[1].Value)
            .Should().Equal("Email", "NormalizedEmail", "IsEmailConfirmed", "PasswordHash", "MustChangePassword");

        sql.Should().Contain($"[Email] = N'{email.Value}'")
            .And.Contain($"[NormalizedEmail] = N'{email.ToNormalized()}'")
            .And.Contain("[IsEmailConfirmed] = 1")
            .And.Contain($"[PasswordHash] = N'{Hash}'")
            .And.Contain("[MustChangePassword] = 0");
        email.Value.Should().Be("operator.name@example.org");
        sql.Should().NotContain(typed.Trim(), "the raw input never reaches the statement");
        sql.Should().NotContain("UPPER(", "normalized in C#, never by SQL");
    }

    // ── T3: a quote is doubled inside N'…', in both address literals ──────

    [Fact]
    public void T3_AnAddressWithAQuote_IsDoubledInBothLiterals()
    {
        var sql = Build("o'brien@example.org");

        sql.Should().Contain("[Email] = N'o''brien@example.org'")
            .And.Contain("[NormalizedEmail] = N'O''BRIEN@EXAMPLE.ORG'");
    }

    // ── T4: the row-count guard follows the UPDATE ─────────────────────────

    [Fact]
    public void T4_TheRowCountGuard_FollowsTheUpdate()
    {
        var sql = Build("operator@example.org");

        var update = sql.IndexOf("UPDATE [dbo].[Users]", StringComparison.Ordinal);
        var guard = sql.IndexOf("IF @@ROWCOUNT <> 1 THROW 50000,", StringComparison.Ordinal);

        update.Should().Be(0);
        guard.Should().BeGreaterThan(update);
        sql[..guard].TrimEnd().Should().EndWith(";", "THROW needs the statement before it to end with a semicolon");
        sql.Should().EndWith("1;");
    }

    // ── T5: refusals ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("admin@company.com")]
    [InlineData("ADMIN@COMPANY.COM")]
    [InlineData("  Admin@Company.com  ")]
    public void T5_TheAddress_IsRefused(string address)
    {
        SeededAdministratorBootstrap.ValidateAddress(address).IsError.Should().BeTrue(address);
    }

    [Fact]
    public void T5_ThePlaceholder_IsRefusedWithItsOwnReason()
    {
        var result = SeededAdministratorBootstrap.ValidateAddress(" ADMIN@company.com");

        result.FirstError.Code.Should().Be("Setup.PlaceholderAddress");
    }

    // ── T6: the builder's Id is the seed's Id, and the seed keys on it ─────

    [Fact]
    public void T6_TheSeededId_IsTheOneThePostDeploymentScriptSeeds()
    {
        var script = File.ReadAllText(Path.Combine(Root, "Auth_DB", "dbo", "PostDeployment", "Script.PostDeployment.sql"));

        var declared = Regex.Match(script, @"DECLARE @AdminUserId UNIQUEIDENTIFIER = '([0-9A-Fa-f-]{36})';");
        declared.Success.Should().BeTrue("the post-deployment script declares the seeded administrator's Id");
        Guid.Parse(declared.Groups[1].Value).Should().Be(SeededAdministratorBootstrap.SeededAdministratorId);

        script.Should().Contain("IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Id] = @AdminUserId)",
            "the seed inserts the administrator keyed on its Id, so changing the address never makes a publish re-insert it");
    }

    // ── T7: the go-live check is keyed on the Id; no placeholder check left ─

    [Fact]
    public void T7_TheGoLiveCheck_IsKeyedOnTheId_AndNoDocumentSelectsThePlaceholder()
    {
        var repository = Path.GetFullPath(Path.Combine(Root, ".."));
        var guide = File.ReadAllText(Path.Combine(repository, "ReadMe", "PRODUCTION_DEPLOYMENT_GUIDE.md"));

        guide.Should().Contain(
            "SELECT [Email], [IsEmailConfirmed], [IsTwoFactorEnabled], CASE WHEN [PasswordHash] IS NULL THEN 'no password' ELSE 'set' END AS [Password] FROM [dbo].[Users] WHERE [Id] = '00000000-0000-0000-0000-000000000002';");

        var documents = Directory.EnumerateFiles(Path.Combine(repository, "ReadMe"), "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(repository, "README.md"));
        foreach (var document in documents)
        {
            File.ReadAllText(document).Should().NotContain("WHERE [Email] = 'admin@company.com'",
                $"{Path.GetFileName(document)} must not check the placeholder: after Auth_Setup runs it matches nothing");
        }
    }

    // ── T8: no placeholder default in the tool ─────────────────────────────

    [Fact]
    public void T8_TheTool_HasNoPlaceholderDefault()
    {
        var setup = Path.Combine(Root, "Auth_Setup");

        foreach (var file in Directory.EnumerateFiles(setup, "*.cs")
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var occurrences = Regex.Matches(File.ReadAllText(file), Regex.Escape(SeededAdministratorBootstrap.PlaceholderAddress), RegexOptions.IgnoreCase).Count;
            var allowed = Path.GetFileName(file) == "SeededAdministratorBootstrap.cs" ? 1 : 0;

            occurrences.Should().Be(allowed,
                $"{Path.GetFileName(file)}: the placeholder may appear only as the refused constant, never as a default");
        }
    }
}
