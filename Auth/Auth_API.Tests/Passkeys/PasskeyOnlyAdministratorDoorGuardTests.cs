using System.Text.RegularExpressions;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B, D-S20-4 (ii-s): who asks the refusal of an administrator whose only usable factor is
/// a passkey, and where. Every file that mints a session through the shared builder either asks
/// it, naming its own door, before its second step, its spending of a first proof, its restore
/// and its session, or is listed here as exempt with its reason. A new sign-in exit fails here
/// until it says which it is.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the password sign-in's refusal removed; the second
/// step's refusal moved after its session; the recoverer asking it of the passkey's own entry
/// too.
/// </remarks>
public class PasskeyOnlyAdministratorDoorGuardTests
{
    /// <summary>The exits that ask, and the door each names.</summary>
    private static readonly Dictionary<string, string> Asks = new()
    {
        ["LoginCommandHandler.cs"] = "Password",
        ["ExternalLoginCommandHandler.cs"] = "ExternalProvider",
        ["SignInWithEmailCodeCommandHandler.cs"] = "EmailCode",
        ["VerifyEmailCommandHandler.cs"] = "VerifyEmail",
        ["VerifyTwoFactorLoginCommandHandler.cs"] = "SecondStep",
        // The password, provider and emailed-code entries of the recovery of an account waiting
        // for deletion, in their shared recoverer. The passkey's own entry passes it by.
        ["AccountDeletionRecoverer.cs"] = "DeletionRecovery",
    };

    /// <summary>The exits that do not ask, and why.</summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["VerifyPasskeyAssertionCommandHandler.cs"] = "the passkey's own sign-in: its proof is the passkey",
        ["CompleteRegistrationCommandHandler.cs"] = "a new account holds no passkey",
        ["ExchangeAuthorizationCodeCommandHandler.cs"] =
            "the code's IdP session was opened through a door that asked; the platform policy withholds the rest (D-S20-4 (ii))",
    };

    /// <summary>What must come after the refusal wherever the file does it.</summary>
    private static readonly string[] AfterTheRefusal =
    [
        @"\.\s*BuildAsync\s*\(",
        @"\.\s*CreateChallengeAsync\s*\(",
        @"\.\s*RecordSuccessfulLogin\s*\(",
        @"\.\s*RequiresPasswordAsync\s*\(",
        @"\.\s*RestoreAsync\s*\(",
        @"\bVerifyTwoFactorCodeAsync\s*\(\s*user\.Id",
        @"await\s+spendFirstProof\s*\(",
    ];

    private static Dictionary<string, string> SessionExits() =>
        ApiSourceScan.ProductionSources()
            .Where(file => !file.File.EndsWith("LoginResponseBuilder.cs", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(file.Source, @"\bILoginResponseBuilder\b")
                && Regex.IsMatch(file.Source, @"\.\s*BuildAsync\s*\("))
            .ToDictionary(file => Path.GetFileName(file.File), file => file.Source);

    [Fact]
    public void EverySessionExit_AsksTheRefusalOrSaysWhyNot()
    {
        SessionExits().Keys.Should().BeEquivalentTo(Asks.Keys.Concat(Exempt.Keys),
            "a new exit must ask the refusal of a passkey-only administrator, or be listed as exempt with its reason");
    }

    [Fact]
    public void EachDoor_AsksWithItsOwnDoor_BeforeAnythingIsSpentChallengedOrMinted()
    {
        var exits = SessionExits();

        foreach (var (name, door) in Asks)
        {
            var source = exits[name];
            var refusals = Regex.Matches(source, @"\.\s*RefusalAsync\s*\(([^;]*?)\)\s*is\s*\{\s*\}");
            refusals.Should().ContainSingle($"{name} asks once");
            refusals[0].Groups[1].Value.Should().Contain($"PasskeyOnlyAdministratorDoors.{door},", name);

            foreach (var marker in AfterTheRefusal)
            {
                foreach (System.Text.RegularExpressions.Match later in Regex.Matches(source, marker))
                {
                    later.Index.Should().BeGreaterThan(refusals[0].Index, $"{name}: {marker} comes after the refusal");
                }
            }
        }
    }

    [Fact]
    public void ThePasskeysOwnDoors_NeverAskIt()
    {
        var sources = ApiSourceScan.ProductionSources()
            .Where(file => file.File.EndsWith("VerifyPasskeyAssertionCommandHandler.cs", StringComparison.Ordinal)
                || file.File.EndsWith("RecoverAccountWithPasskeyCommandHandler.cs", StringComparison.Ordinal))
            .ToList();

        sources.Should().HaveCount(2);
        // Neither holds the rule nor asks it (a mention in a comment is neither).
        sources.Should().OnlyContain(file => !Regex.IsMatch(file.Source, @"\.\s*RefusalAsync\s*\(")
            && !Regex.IsMatch(file.Source, @"\bPasskeyOnlyAdministratorRule\s+\w+\s*[,;)]"));

        // The recoverer passes it by for the one entry whose proof satisfies two factors.
        var recoverer = SessionExits()["AccountDeletionRecoverer.cs"];
        recoverer.Should().Contain("var passkeyEntry = primaryMethod.IsMfaSatisfied;");
        Regex.IsMatch(recoverer, @"if\s*\(\s*!passkeyEntry\s*&&\s*await\s+_passkeyOnlyAdministrator\.RefusalAsync\(")
            .Should().BeTrue("only the passkey's entry is let past the refusal");
    }
}
