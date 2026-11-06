using System.Text.RegularExpressions;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S20 PR B (R-EVT): the sign-in event says how the person signed in. Every exit that records a
/// sign-in (<c>User.RecordSuccessfulLogin</c>, which raises <c>UserLoggedInEvent</c>) passes the
/// very methods its session records, so the audit line and the session never disagree. The two
/// exits that mint a session without recording a sign-in are listed with their reason.
/// </summary>
/// <remarks>
/// Deliberate break, verified red: the password sign-in recording <c>AuthenticationMethods.Unknown</c>
/// while its session records the password.
/// </remarks>
public class UserLoggedInMethodsGuardTests
{
    /// <summary>Exits that build a session and record no sign-in, and why (T-B7).</summary>
    private static readonly Dictionary<string, string> RecordNoSignIn = new()
    {
        ["AccountDeletionRecoverer.cs"] = "a recovery publishes AccountDeletionCancelledEvent, its own audit line",
        ["ExchangeAuthorizationCodeCommandHandler.cs"] = "the code's IdP session was recorded at its own sign-in",
    };

    [Fact]
    public void EveryRecordedSignIn_CarriesTheMethodsItsSessionRecords()
    {
        var exits = ApiSourceScan.ProductionSources()
            .Where(file => !file.File.EndsWith("LoginResponseBuilder.cs", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(file.Source, @"\bILoginResponseBuilder\b")
                && Regex.IsMatch(file.Source, @"\.\s*BuildAsync\s*\("))
            .ToDictionary(file => Path.GetFileName(file.File), file => file.Source);

        exits.Keys.Should().Contain(RecordNoSignIn.Keys);

        foreach (var (name, source) in exits)
        {
            var records = Regex.Matches(source, @"\.\s*RecordSuccessfulLogin\s*\(");
            if (RecordNoSignIn.ContainsKey(name))
            {
                records.Should().BeEmpty($"{name}: {RecordNoSignIn[name]}");
                continue;
            }

            records.Should().ContainSingle($"{name} records its sign-in once");
            var build = Regex.Match(source, @"\.\s*BuildAsync\s*\(");
            Argument(source, records[0].Index, 0).Should().Be(Argument(source, build.Index, 4),
                $"{name}: the event and the session record the same methods");
        }
    }

    [Fact]
    public void NoSignIn_IsRecordedWithoutItsMethods()
    {
        // The event's methods default to Unknown for the entity's older callers; no exit relies on it.
        ApiSourceScan.ProductionSources()
            .SelectMany(file => Regex.Matches(file.Source, @"\.\s*RecordSuccessfulLogin\s*\(")
                .Select(call => (Name: Path.GetFileName(file.File), First: Argument(file.Source, call.Index, 0))))
            .Should().NotContain(call => call.First.Contains("Unknown", StringComparison.Ordinal)
                || call.First.StartsWith("request.", StringComparison.Ordinal));
    }

    /// <summary>The argument at <paramref name="position"/> of the call that starts at <paramref name="call"/>.</summary>
    private static string Argument(string source, int call, int position)
    {
        var i = source.IndexOf('(', call) + 1;
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

        return Regex.Replace(args[position], @"\s+", string.Empty);
    }
}
