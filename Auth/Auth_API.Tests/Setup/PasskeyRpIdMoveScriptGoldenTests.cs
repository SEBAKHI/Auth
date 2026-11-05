using Auth.Domain.Constants;
using Auth_API.Tests.Infrastructure;
using Auth_Setup;

namespace Auth_API.Tests.Setup;

/// <summary>
/// S20 ([S20-O10], FA11 (11-3)) and PK12: the owner's two scripts that revoke passkeys outside
/// the API. Both match a golden file byte for byte, revoke only live passkeys, and write one
/// audit row per passkey revoked, since no event or notice follows a script.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the passkey statements dropped from
/// <c>BreakGlassScript.Build</c>; the audit insert dropped from <c>PasskeyRevocation</c>;
/// <c>AND [RevokedAt] IS NULL</c> dropped; the RP ID rule bypassed in <c>PasskeyRpIdMoveScript</c>.
/// </remarks>
public class PasskeyRpIdMoveScriptGoldenTests
{
    private static string Golden(string fileName) =>
        File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API.Tests", "Setup", fileName))
            .Replace("\r\n", "\n")
            .TrimEnd('\n');

    [Fact]
    public void Build_MatchesTheGoldenFile_ForAnRpIdWithCapitalsAndSpaces()
    {
        // Normalized as the API normalizes Passkeys:RpId: trimmed and lower-cased.
        PasskeyRpIdMoveScript.Build("  Example.COM ").Should().Be(Golden("PasskeyRpIdMoveScript.golden.sql"));
    }

    [Fact]
    public void Build_IsOneTransaction_ThatRefusesWithoutThePasskeyTable_AndRetiresOnlyLivePasskeysOfThatRpId()
    {
        var script = PasskeyRpIdMoveScript.Build("example.com")!;
        var statement = script[script.IndexOf("SET XACT_ABORT ON;", StringComparison.Ordinal)..];

        statement.Should().StartWith("SET XACT_ABORT ON; BEGIN TRANSACTION;").And.EndWith("COMMIT;");
        statement.Should().Contain("IF OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NULL THROW 50002",
            "a database without the passkey table must change nothing, loudly");
        statement.Should().Contain("WHERE [RpId] = N'example.com' AND [RevokedAt] IS NULL;",
            "only the live passkeys of the RP ID being left");
        statement.Should().Contain($"[RevokedReason] = N'{PasskeyRemovalReasons.RpIdMove}'")
            .And.Contain($"N'{PasskeyRemovalReasons.RpIdMove}' AS [reason]");
        script.Split('\n').TakeWhile(line => !line.StartsWith("SET ", StringComparison.Ordinal))
            .Should().OnlyContain(line => line.StartsWith("-- ", StringComparison.Ordinal), "the instructions are comments only");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example")]
    [InlineData("https://example.com")]
    [InlineData("example.com/")]
    [InlineData("10.0.0.1")]
    [InlineData("o'neil.example.com")]
    [InlineData("exa mple.com")]
    [InlineData("example.com.")]
    public void Build_RefusesWhatIsNotAnRpId(string rpId)
    {
        PasskeyRpIdMoveScript.Build(rpId).Should().BeNull("no SQL is built from what cannot be an RP ID");
    }

    [Fact]
    public void Build_AcceptsLocalhost()
    {
        PasskeyRpIdMoveScript.Build("localhost").Should().Contain("WHERE [RpId] = N'localhost' AND [RevokedAt] IS NULL;");
    }

    [Fact]
    public void BreakGlass_RevokesTheAccountsLivePasskeys_InTheSameTransaction_WithOneAuditRowEach()
    {
        var script = BreakGlassScript.Build("someone@example.org").Value;
        var start = script.IndexOf("SET XACT_ABORT ON;", StringComparison.Ordinal);
        var passkeys = script.IndexOf("IF OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NOT NULL", StringComparison.Ordinal);

        passkeys.Should().BeGreaterThan(start, "inside the transaction")
            .And.BeLessThan(script.IndexOf("COMMIT;", StringComparison.Ordinal));
        script.Should().Contain(
            $"UPDATE [dbo].[UserPasskeys] SET [RevokedAt] = SYSUTCDATETIME(), [RevokedReason] = N'{PasskeyRemovalReasons.BreakGlass}', [RevokedBy] = NULL\n"
            + "     OUTPUT inserted.[Id], inserted.[UserId], inserted.[Name] INTO @RevokedPasskeys\n"
            + "     WHERE [UserId] = @U AND [RevokedAt] IS NULL;",
            "only the account's live passkeys, each into the audit rows");
        script.Should().NotContain("THROW 50002", "a database without the passkey table still gets the reset");
    }

    [Theory]
    [InlineData(PasskeyRemovalReasons.BreakGlass)]
    [InlineData(PasskeyRemovalReasons.RpIdMove)]
    public void BothScripts_WriteTheAuditRowTheApiWrites_DetailsBuiltByForJson(string reason)
    {
        var script = reason == PasskeyRemovalReasons.BreakGlass
            ? BreakGlassScript.Build("someone@example.org").Value
            : PasskeyRpIdMoveScript.Build("example.com")!;

        script.Should().Contain("OUTPUT inserted.[Id], inserted.[UserId], inserted.[Name] INTO @RevokedPasskeys",
            "the audit rows are for exactly the rows the update revoked");
        script.Should().Contain(
            "INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [EntityType], [EntityId], [Details], [ActionType], [IsSuccess])\n"
            + $"    SELECT r.[UserId], N'{AuditActions.PasskeyRemoved}', N'UserPasskey', r.[Id],\n"
            + $"           (SELECT r.[Name] AS [name], N'{reason}' AS [reason] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),\n"
            + $"           N'{AuditActionTypes.Security}', 1\n"
            + "    FROM @RevokedPasskeys r;");
        script.Should().NotContain("+ r.[Name]", "a name is the owner's text: JSON is built by FOR JSON, never by concatenation");
    }
}
