using System.Text.RegularExpressions;
using Match = System.Text.RegularExpressions.Match;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12, LK5 and owner decision G8 (2026-09-27): a completed sign-in lifts an
/// automatic lock and starts a new failure count only when it proved the password
/// in that same sign-in. <c>passwordProven</c> has no default, so the compiler makes
/// every caller decide; this pins WHAT each one decides, read from its source, so a
/// renewal or another factor can never start lifting the lock unnoticed.
/// </summary>
/// <remarks>
/// <para>
/// True: the password form (the familiar-source door included), and the
/// second-factor step when the password opened its challenge (an Unknown or
/// provider primary does not count; VerifyTwoFactorLoginCommandHandlerTests holds the
/// cases), and the password entry of a pending-deletion recovery. A completed
/// change-password lifts the lock through its own statement (ClearAutomaticLockAsync),
/// not through the builder.
/// </para>
/// <para>
/// False: a provider, an emailed code, a completed registration, the code exchange,
/// and a provider recovery. Seven callers today; the passkey command of P7 will be
/// the eighth, with false.
/// </para>
/// <para>
/// A caller is any file that names the builder, by its interface or its concrete
/// type, and each call's <c>passwordProven:</c> is read inside that call's own
/// parentheses (review 64, F5). Who may call the lockout's statements directly is
/// <c>LockStatementCallersSourceGuardTests</c>.
/// </para>
/// <para>Deliberate break: pass <c>true</c> from the code exchange.</para>
/// </remarks>
public class BuildAsyncCallersRepairGuardTests
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["LoginCommandHandler.cs"] = "true",
        ["VerifyTwoFactorLoginCommandHandler.cs"] = "challenge.PrimaryMethod.ProvesPassword",
        ["AccountDeletionRecoverer.cs"] = "primaryMethod.ProvesPassword",
        ["ExternalLoginCommandHandler.cs"] = "false",
        ["VerifyEmailCommandHandler.cs"] = "false",
        ["CompleteRegistrationCommandHandler.cs"] = "false",
        // OI-215: an emailed code proves the mailbox, never the password (G8).
        ["SignInWithEmailCodeCommandHandler.cs"] = "false",
        ["ExchangeAuthorizationCodeCommandHandler.cs"] = "false",
    };

    private static List<(string Name, string Argument)> Callers()
    {
        var callers = new List<(string, string)>();
        foreach (var (file, source) in ApiSourceScan.ProductionSources())
        {
            if (file.EndsWith("LoginResponseBuilder.cs", StringComparison.Ordinal)
                || !Regex.IsMatch(source, @"\bI?LoginResponseBuilder\b"))
            {
                continue;
            }

            foreach (Match call in Regex.Matches(source, @"\.\s*BuildAsync\s*\("))
            {
                var arguments = Arguments(source, call.Index + call.Length - 1);
                var argument = Regex.Match(arguments, @"passwordProven:\s*([^,]+?)\s*(?:,|$)");
                callers.Add((Path.GetFileName(file), argument.Success ? argument.Groups[1].Value.Trim() : "<none>"));
            }
        }

        return callers;
    }

    /// <summary>The text between the parenthesis at <paramref name="open"/> and its match.</summary>
    internal static string Arguments(string source, int open)
    {
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return source[(open + 1)..i];
            }
        }

        return source[(open + 1)..];
    }

    [Theory]
    [InlineData("x.BuildAsync(user, ip, passwordProven: true, ct)", "user, ip, passwordProven: true, ct")]
    [InlineData("x.BuildAsync(user, f(a, b), ct); y.Other(passwordProven: true, c)", "user, f(a, b), ct")]
    public void Arguments_StopsAtTheCallsOwnParenthesis(string source, string expected)
    {
        Arguments(source, source.IndexOf('(')).Should().Be(expected);
    }

    [Fact]
    public void EveryCompletedSignIn_SaysWhetherItProvedThePassword()
    {
        var callers = Callers();

        callers.Should().HaveCount(8, "eight sign-in exits today; a new one is listed here with its reason");
        callers.Select(c => c.Name).Should().BeEquivalentTo(Expected.Keys);
        foreach (var (name, argument) in callers)
        {
            argument.Should().Be(Expected[name], $"{name} decides whether its success lifts the lock");
        }
    }

    [Fact]
    public void ChangePassword_LiftsTheLockThroughItsOwnStatement()
    {
        var source = ApiSourceScan.ProductionSources()
            .Single(file => file.File.EndsWith("ChangePasswordCommandHandler.cs", StringComparison.Ordinal)).Source;

        source.Should().Contain("_accountLockout.ClearAutomaticLockAsync(request.UserId, cancellationToken)");
        source.IndexOf("UpdatePasswordAsync", StringComparison.Ordinal).Should().BeLessThan(
            source.IndexOf("ClearAutomaticLockAsync", StringComparison.Ordinal),
            "the lock is lifted after the new password is written, never before");
    }
}
