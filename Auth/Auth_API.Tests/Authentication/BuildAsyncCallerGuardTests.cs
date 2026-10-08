using System.Text.RegularExpressions;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S08 T4: every sign-in exit says what it proved. The builder's
/// <c>authenticationMethods</c> parameter has no default, so the compiler already
/// forces each caller to pass one; this pins WHICH value each passes — the factors
/// that request actually verified — and that only the authorization-code exchange
/// passes Unknown. A new caller fails here until it is added with its reason.
/// </summary>
public class BuildAsyncCallerGuardTests
{
    /// <summary>
    /// The callers, and the expression each passes, read from its source. The table
    /// in the PR is this list.
    /// </summary>
    private static readonly Dictionary<string, string> Expected = new()
    {
        // The password was checked.
        ["LoginCommandHandler.cs"] = "AuthenticationMethods.Password",
        // The provider vouched for the person.
        ["ExternalLoginCommandHandler.cs"] = "AuthenticationMethods.ExternalIdentity",
        // An emailed code alone: never a first factor (D2).
        ["VerifyEmailCommandHandler.cs"] = "AuthenticationMethods.EmailCode",
        // Verify-first registration: the emailed code and the password just set.
        ["CompleteRegistrationCommandHandler.cs"] = "AuthenticationMethods.Password.With(AuthenticationMethods.EmailCode)",
        // The first factor recorded on the challenge, and the second just settled.
        ["VerifyTwoFactorLoginCommandHandler.cs"] = "methods",
        // The entry point's first factor, with the TOTP code when one was claimed.
        ["AccountDeletionRecoverer.cs"] = "methods",
        // The application token exchange: the code does not carry the IdP session's
        // methods yet (S23), and an application token has no platform authority.
        ["ExchangeAuthorizationCodeCommandHandler.cs"] = "AuthenticationMethods.Unknown",
    };

    private static List<(string Name, string Source)> Callers() =>
        ApiSourceScan.ProductionSources()
            .Where(file => !file.File.EndsWith("LoginResponseBuilder.cs", StringComparison.Ordinal)
                           && !file.File.EndsWith("ILoginResponseBuilder.cs", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(file.Source, @"_loginResponseBuilder\.BuildAsync\("))
            .Select(file => (Path.GetFileName(file.File), file.Source))
            .ToList();

    /// <summary>The fifth argument of the one BuildAsync call in a source.</summary>
    private static string MethodsArgument(string source)
    {
        var start = source.IndexOf("_loginResponseBuilder.BuildAsync(", StringComparison.Ordinal);
        var i = source.IndexOf('(', start) + 1;
        var depth = 0;
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        for (; i < source.Length; i++)
        {
            var ch = source[i];
            if (ch == '(') depth++;
            if (ch == ')' && depth-- == 0) { args.Add(current.ToString()); break; }
            if (ch == ',' && depth == 0) { args.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }

        return Regex.Replace(args[4], @"\s+", string.Empty);
    }

    [Fact]
    public void EverySignInExit_PassesWhatItVerified()
    {
        var callers = Callers();

        callers.Select(c => c.Name).Should().BeEquivalentTo(Expected.Keys,
            "a new sign-in exit must say what it proved, and be listed here with its reason");

        foreach (var (name, source) in callers)
        {
            Regex.Matches(source, @"_loginResponseBuilder\.BuildAsync\(").Should().HaveCount(1, name);
            MethodsArgument(source).Should().Be(Regex.Replace(Expected[name], @"\s+", string.Empty), name);
        }
    }

    [Fact]
    public void OnlyTheExchange_PassesUnknown()
    {
        Callers()
            .Where(c => MethodsArgument(c.Source).Contains("AuthenticationMethods.Unknown", StringComparison.Ordinal))
            .Select(c => c.Name)
            .Should().Equal(["ExchangeAuthorizationCodeCommandHandler.cs"],
                "Unknown asks a platform administrator to sign in again; only the application exchange may pass it");
    }

    [Fact]
    public void TheMergedExits_BuildTheirMethodsFromWhatTheyVerified()
    {
        var sources = Callers().ToDictionary(c => c.Name, c => c.Source);

        sources["VerifyTwoFactorLoginCommandHandler.cs"].Should().Contain(
            "challenge.PrimaryMethod.With(AuthenticationMethods.From(proof.Value.Method))");
        sources["AccountDeletionRecoverer.cs"].Should().Contain("var methods = primaryMethod;")
            .And.Contain("methods = methods.With(verified.Value);");
    }

    [Fact]
    public void TheChallengeGates_RecordTheirFirstFactor()
    {
        // The three gates that open a challenge record the factor that opened it,
        // so the verify step can merge it with the second.
        var expected = new Dictionary<string, string>
        {
            ["LoginCommandHandler.cs"] = "AuthenticationMethods.Password",
            ["ExternalLoginCommandHandler.cs"] = "AuthenticationMethods.ExternalIdentity",
            ["VerifyEmailCommandHandler.cs"] = "AuthenticationMethods.EmailCode",
        };

        var gates = ApiSourceScan.ProductionSources()
            .Where(file => file.Source.Contains("_twoFactorChallengeService.CreateChallengeAsync(", StringComparison.Ordinal))
            .ToDictionary(file => Path.GetFileName(file.File), file => file.Source);

        gates.Keys.Should().BeEquivalentTo(expected.Keys);
        foreach (var (name, method) in expected)
        {
            Regex.IsMatch(gates[name], @"CreateChallengeAsync\(\s*user,\s*request\.IpAddress,\s*request\.UserAgent,\s*" + Regex.Escape(method) + @",")
                .Should().BeTrue(name);
        }
    }
}
