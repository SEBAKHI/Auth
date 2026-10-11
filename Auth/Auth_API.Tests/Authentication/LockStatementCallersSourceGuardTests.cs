using System.Text.RegularExpressions;
using Match = System.Text.RegularExpressions.Match;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12, review 64 F5: who may call the statements that count a wrong password,
/// lift a lock or start a new count. Owner decision G8 names the exits (a sign-in
/// that proved the password, a completed reset, a change-password, an
/// administrator), and <c>BuildAsyncCallersRepairGuardTests</c> pins the builder's
/// callers; this pins the rest, read from the production source, so a provider,
/// emailed-code or token path that starts calling one of them fails here first.
/// </summary>
/// <remarks>
/// <para>
/// Each call <c>receiver.Method(</c> is attributed to the type its receiver is
/// declared with in the same file (a field, parameter or local), and a receiver of
/// unknown type fails the test rather than slipping through. The names are the
/// methods as they are today: rename the list, not the code.
/// </para>
/// <para>
/// Deliberate break: add <c>await _accountLockout.ClearAutomaticLockAsync(user.Id,
/// cancellationToken);</c> to ExternalLoginCommandHandler's familiar branch.
/// </para>
/// </remarks>
public class LockStatementCallersSourceGuardTests
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        // The lockout's own writes: one class each owns the decision to call them.
        ["IAccountLockout.RecordPasswordFailureAsync"] = ["LoginCommandHandler.cs"],
        ["IAccountLockout.RecordSignInAsync"] = ["LoginResponseBuilder.cs"],
        ["IAccountLockout.ClearAutomaticLockAsync"] = ["ResetPasswordCommandHandler.cs", "ChangePasswordCommandHandler.cs"],
        ["IAccountLockout.RepairLapsedLockAsync"] =
            ["LoginCommandHandler.cs", "ExternalLoginCommandHandler.cs", "VerifyEmailCommandHandler.cs"],

        // The repository's statements: only through the lockout, which applies the
        // normalized limits and writes the signals.
        ["IUserRepository.RecordSuccessfulLoginAsync"] = ["AccountLockout.cs"],
        ["IUserRepository.ClearAutomaticLockAsync"] = ["AccountLockout.cs"],
        ["IUserRepository.RepairLapsedLockAsync"] = ["AccountLockout.cs"],
        ["IUserRepository.RecordFailedLoginAsync"] = ["AccountLockout.cs"],

        // An administrator's status action.
        ["IUserRepository.SetAccountStatusAsync"] =
        [
            "LockAccountCommandHandler.cs", "UnlockAccountCommandHandler.cs",
            "ActivateAccountCommandHandler.cs", "DeactivateAccountCommandHandler.cs",
        ],
    };

    private static readonly string[] Methods =
        Allowed.Keys.Select(key => key[(key.IndexOf('.') + 1)..]).Distinct().ToArray();

    private static readonly Regex Call = new(
        $@"\b(?<receiver>\w+)\s*\.\s*(?<method>{string.Join('|', Methods)})\s*\(");

    /// <summary>Every call of a pinned method, as (type.method, file); an unknown receiver type reads "?".</summary>
    internal static List<(string Statement, string File)> Calls(string fileName, string source)
    {
        var calls = new List<(string, string)>();
        foreach (Match call in Call.Matches(source))
        {
            var receiver = call.Groups["receiver"].Value;
            var declared = Regex.Match(
                source, $@"\b(IAccountLockout|AccountLockout|IUserRepository|UserRepository)\??\s+{Regex.Escape(receiver)}\b");
            var type = declared.Success ? "I" + declared.Groups[1].Value.TrimStart('I') : "?";
            calls.Add(($"{type}.{call.Groups["method"].Value}", fileName));
        }

        return calls;
    }

    private static List<(string Statement, string File)> ProductionCalls() =>
        ApiSourceScan.ProductionSources()
            .SelectMany(file => Calls(Path.GetFileName(file.File), file.Source))
            .ToList();

    [Fact]
    public void EveryCallOfALockStatement_ComesFromAnAllowedCaller()
    {
        var calls = ProductionCalls();

        calls.Should().NotBeEmpty();
        foreach (var (statement, file) in calls)
        {
            Allowed.Should().ContainKey(statement, $"{file} calls {statement}, a receiver this guard cannot place");
            Allowed[statement].Should().Contain(file, $"{file} may not call {statement} (owner decision G8 names the exits)");
        }
    }

    [Fact]
    public void EveryAllowedCaller_StillCallsIt()
    {
        // A list that no longer matches the code pins nothing.
        var calls = ProductionCalls().ToHashSet();

        foreach (var (statement, files) in Allowed)
        {
            foreach (var file in files)
            {
                calls.Should().Contain((statement, file), $"{file} is listed as calling {statement}");
            }
        }
    }

    [Theory]
    [InlineData("private readonly IAccountLockout _lock; void M() { _lock.ClearAutomaticLockAsync(id, ct); }", "IAccountLockout.ClearAutomaticLockAsync")]
    [InlineData("private readonly IUserRepository _users; void M() { _users . ClearAutomaticLockAsync (id, ct); }", "IUserRepository.ClearAutomaticLockAsync")]
    [InlineData("void M(IUserRepository repo) => repo.SetAccountStatusAsync(id, change, actor, ct);", "IUserRepository.SetAccountStatusAsync")]
    [InlineData("void M() { other.RepairLapsedLockAsync(id, ct); }", "?.RepairLapsedLockAsync")]
    public void Calls_AttributesEachShapeToItsReceiversType(string source, string expected)
    {
        Calls("Example.cs", source).Should().Equal([(expected, "Example.cs")]);
    }
}
