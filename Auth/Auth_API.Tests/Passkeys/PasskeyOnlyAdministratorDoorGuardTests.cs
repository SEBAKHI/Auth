using System.Text.RegularExpressions;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B, D-S20-4 (ii-s): who asks the refusal of an administrator whose only usable factor is
/// a passkey, and where. Every file that mints a session through the shared builder either asks
/// it, naming its own door, after its own proof and before its second step, its spending of a
/// first proof, its restore and its session, or is listed here as exempt with its reason. A new
/// sign-in exit fails here until it says which it is.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the password sign-in's refusal removed; the second
/// step's refusal moved after its session; the recoverer asking it of the passkey's own entry
/// too; the password sign-in's refusal moved above the password check (REVIEW 151 F5: whoever
/// knows the address would learn the account is a passkey-only administrator, and the wrong
/// password would go uncounted); the emailed-code recovery's refusal moved after its password
/// step (F10, S6).
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

    /// <summary>
    /// What must come BEFORE the refusal, per door: its own proof (REVIEW 151 F5). The recoverer has
    /// none of its own: its entries prove the person before they call it.
    /// </summary>
    private static readonly Dictionary<string, string[]> BeforeTheRefusal = new()
    {
        // The door checks the password and judges the lock again after the hash (PasswordDoor).
        ["LoginCommandHandler.cs"] = [@"_passwordDoor\s*\.\s*CheckAsync\s*\("],
        ["ExternalLoginCommandHandler.cs"] = [@"\.\s*ValidateTokenAsync\s*\("],
        ["SignInWithEmailCodeCommandHandler.cs"] = [@"\.\s*TryConsumeSignInCodeAsync\s*\("],
        ["VerifyEmailCommandHandler.cs"] = [@"\.\s*TryConsumeAsync\s*\("],
        ["VerifyTwoFactorLoginCommandHandler.cs"] = [@"\.\s*TryCommitLoginAsync\s*\("],
        ["AccountDeletionRecoverer.cs"] = [],
    };

    /// <summary>
    /// How many times a door asks: the recoverer asks for the recovery and, before its emailed code
    /// is sent, for the request of that code (REVIEW 151 F10, C9).
    /// </summary>
    private static readonly Dictionary<string, int> TimesAsked = new()
    {
        ["AccountDeletionRecoverer.cs"] = 2,
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

    /// <summary>
    /// A signed-in person proving themself again mints no session, so the scan above does not see
    /// it: pinned by name. It asks before it reads what the account takes, and before anything is
    /// hashed or counted: the caller is signed in as that account, so there is nobody to hide the
    /// answer from.
    /// </summary>
    [Fact]
    public void TheProofInsideASession_AsksItsOwnDoor_BeforeAnyProof()
    {
        var source = ApiSourceScan.ProductionSources()
            .Single(file => Path.GetFileName(file.File) == "ReauthenticateCommandHandler.cs").Source;

        var refusal = Regex.Match(source, @"\.\s*RefusalAsync\s*\(\s*user\.Id,\s*PasskeyOnlyAdministratorDoors\.Reauthentication");
        refusal.Success.Should().BeTrue();
        refusal.Index.Should().BeLessThan(Regex.Match(source, @"await\s+ProvePasswordAsync\s*\(").Index);
        refusal.Index.Should().BeLessThan(Regex.Match(source, @"await\s+ProveEmailCodeAsync\s*\(").Index);
    }

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
            refusals.Should().HaveCount(TimesAsked.GetValueOrDefault(name, 1), $"{name} asks where it says");
            refusals.Should().OnlyContain(refusal => refusal.Groups[1].Value.Contains($"PasskeyOnlyAdministratorDoors.{door},"), name);

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
    public void EachDoor_AsksAfterItsOwnProof()
    {
        // REVIEW 151 F5: a refusal moved above the proof would answer anyone who knows the address
        // whether the account is a passkey-only administrator.
        var exits = SessionExits();
        BeforeTheRefusal.Keys.Should().BeEquivalentTo(Asks.Keys, "every door that asks says what proves the person first");

        foreach (var (name, proofs) in BeforeTheRefusal)
        {
            var refusal = Regex.Match(exits[name], @"\.\s*RefusalAsync\s*\(");
            foreach (var proof in proofs)
            {
                var first = Regex.Match(exits[name], proof);
                first.Success.Should().BeTrue($"{name} proves the person with {proof}");
                first.Index.Should().BeLessThan(refusal.Index, $"{name}: {proof} comes before the refusal");
            }
        }
    }

    [Fact]
    public void TheEmailedCodeRecovery_AsksAfterItsCodeCheck_AndBeforeItsPasswordStep()
    {
        // REVIEW 151 F10 (S6): as the sign-in by code asks it, so a passkey-only administrator is
        // not sent on to a password entry that would refuse in turn.
        var source = ApiSourceScan.ProductionSources()
            .Single(file => file.File.EndsWith("RecoverAccountWithEmailCodeCommandHandler.cs", StringComparison.Ordinal))
            .Source;

        var refusals = Regex.Matches(source, @"\.\s*RefusalAsync\s*\(([^;]*?)\)\s*is\s*\{\s*\}");
        refusals.Should().ContainSingle().Which.Groups[1].Value.Should().Contain("PasskeyOnlyAdministratorDoors.DeletionRecovery,");
        Regex.Match(source, @"\.\s*CheckAsync\s*\(").Index.Should().BeLessThan(refusals[0].Index, "after the code check");
        Regex.Match(source, @"\.\s*RequiresPasswordAsync\s*\(").Index.Should().BeGreaterThan(refusals[0].Index, "before the password step");
        Regex.Match(source, @"\.\s*RecoverAsync\s*\(").Index.Should().BeGreaterThan(refusals[0].Index, "before the shared recovery");
    }

    [Fact]
    public void TheRecoverysEmailCodeSender_AsksBeforeAnyChallengeOrEmail()
    {
        // REVIEW 151 F10 (C9): no code is mailed for a recovery the rule then refuses.
        var source = SessionExits()["AccountDeletionRecoverer.cs"];
        var sender = source.IndexOf("public async Task<ErrorOr<EmailCodeSent>> SendEmailCodeAsync(", StringComparison.Ordinal);
        sender.Should().BePositive();
        var body = source[sender..];
        var refusal = Regex.Match(body, @"\.\s*RefusalAsync\s*\(([^;]*?)\)\s*is\s*\{\s*\}");
        refusal.Success.Should().BeTrue("the sender asks the refusal");
        refusal.Groups[1].Value.Should().Contain("PasskeyOnlyAdministratorDoors.DeletionRecovery,");
        Regex.Match(body, @"_challengeRepository\.\s*CreateAsync\s*\(").Index.Should().BeGreaterThan(refusal.Index, "before the challenge");
        Regex.Match(body, @"_emailCodeSender\.\s*SendAsync\s*\(").Index.Should().BeGreaterThan(refusal.Index, "before the email");
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
