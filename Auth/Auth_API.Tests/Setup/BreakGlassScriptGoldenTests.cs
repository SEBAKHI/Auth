using Auth_API.Tests.Infrastructure;
using Auth_Setup;

namespace Auth_API.Tests.Setup;

/// <summary>
/// S08 T13: the owner's emergency script is one transaction keyed on the address
/// the sign-in lookup uses, and it matches a golden file byte for byte — so any
/// change to what the owner runs against production is a reviewed change to that
/// file, not a side effect.
/// </summary>
public class BreakGlassScriptGoldenTests
{
    private static string Golden() =>
        File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API.Tests", "Setup", "BreakGlassScript.golden.sql"))
            .Replace("\r\n", "\n")
            .TrimEnd('\n');

    [Fact]
    public void Build_MatchesTheGoldenFile_ForAnAddressWithAQuote()
    {
        // Mixed case and surrounding spaces: normalized exactly as the lookup does.
        var script = BreakGlassScript.Build("  O'Neil@Example.org ");

        script.IsError.Should().BeFalse();
        script.Value.Should().Be(Golden());
    }

    [Fact]
    public void Build_IsOneTransaction_KeyedOnTheNormalizedAddressOfALiveAccount()
    {
        // X12 PR B: comment lines come first (the hard-lock rule, for the owner);
        // what runs is the transaction after them, unchanged.
        var script = Statement(BreakGlassScript.Build("someone@example.org").Value);

        script.Should().StartWith("SET XACT_ABORT ON; BEGIN TRANSACTION;")
            .And.EndWith("COMMIT;");
        script.Should().Contain("WHERE [NormalizedEmail] = N'SOMEONE@EXAMPLE.ORG' AND [IsDeleted] = 0")
            .And.NotContain("[Email] =", "the unique key is the normalized address; [Email] is display text")
            .And.NotContain("UPPER(", "normalized in C#, the way the lookup does, never by SQL");
        script.Should().Contain("IF @U IS NULL THROW 50001",
            "an address no live account has must change nothing, loudly");
    }

    [Fact]
    public void Build_QuoteCannotEndTheLiteral()
    {
        var script = BreakGlassScript.Build("a';DROP@x.io").Value;

        script.Should().Contain("N'A'';DROP@X.IO'");
        script.Should().NotContain("N'A';", "a single quote would end the literal and start a statement");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public void Build_RefusesWhatIsNotAnAddress(string address)
    {
        BreakGlassScript.Build(address).IsError.Should().BeTrue();
    }

    /// <summary>
    /// X12 PR B (ARR-0639): the emergency script has no 403 — the owner runs it — so
    /// it carries the administrator's reset rule (LK4 (4)) as comments before the
    /// transaction: a hard-locked factor only after the owner's password reset later
    /// than ModifiedAt, with a read-only check for the address. Comments only: every
    /// line before the transaction is one, so nothing new executes.
    /// </summary>
    [Fact]
    public void Build_StatesTheHardLockRule_AsCommentsOnly_BeforeTheTransaction()
    {
        var script = BreakGlassScript.Build("someone@example.org").Value;
        var lines = script.Split('\n');
        var transaction = Array.FindIndex(lines, line => line.StartsWith("SET XACT_ABORT ON;", StringComparison.Ordinal));

        transaction.Should().BePositive("the rule comes before the statement");
        lines[..transaction].Should().OnlyContain(line => line.StartsWith("-- ", StringComparison.Ordinal),
            "the owner's script runs nothing it did not run before");

        var comments = string.Join("\n", lines[..transaction]);
        comments.Should().Contain("Password:ConsecutiveFailureLimit")
            .And.Contain("password reset by email LATER than")
            .And.Contain("LastPasswordChangeUtc > ModifiedAt");
        comments.Should().Contain(
            "-- SELECT t.[FailedAttempts], t.[ModifiedAt], u.[LastPasswordChangeUtc] FROM [dbo].[Users] u LEFT JOIN [dbo].[TwoFactorAuth] t ON t.[UserId] = u.[Id] WHERE u.[NormalizedEmail] = N'SOMEONE@EXAMPLE.ORG' AND u.[IsDeleted] = 0;",
            "the check is for the same live account the transaction resolves, and reads only");
    }

    /// <summary>The script from its transaction on: what the owner's run executes.</summary>
    private static string Statement(string script) =>
        script[script.IndexOf("SET XACT_ABORT ON;", StringComparison.Ordinal)..];
}
