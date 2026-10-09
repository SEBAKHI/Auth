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
        var script = BreakGlassScript.Build("someone@example.org").Value;

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
}
