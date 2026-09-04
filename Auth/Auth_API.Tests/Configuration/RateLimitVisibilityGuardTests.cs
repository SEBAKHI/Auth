namespace Auth_API.Tests.Configuration;

/// <summary>
/// A throttle nobody can observe is a throttle nobody can size.
/// </summary>
/// <remarks>
/// Both hosts refused requests silently. Neither rejection handler wrote a log
/// line, so the only trace a refusal left was a generic request-completed entry
/// carrying a 429 — which cannot say which of the several allowances ran out,
/// and they differ by an order of magnitude. The practical consequence is that
/// every limit in this system is a console-editable number whose effect could
/// not be evaluated after it was changed: a limit lowered onto real users
/// produced silence, and the only symptom was that sign-ups ran slower than
/// somebody expected.
///
/// <para>
/// The second half is the same absence facing the other way. The API's 429 body
/// omitted the <c>status</c> field, and the SPA derives an error's kind from
/// that field alone — so a refusal from this host was classified "unknown" and
/// the user was told to contact support, while the identical refusal from the
/// gateway, whose body carries the field, correctly told them to wait a moment.
/// Two hosts refusing one request for one reason must say so identically.
/// </para>
///
/// <para>
/// These read source text rather than exercising a pipeline, like
/// <see cref="RegistrationThrottleGuardTests"/>: what is asserted is an
/// arrangement across four files in two processes and one language boundary,
/// and each file is entirely defensible on its own.
/// </para>
/// </remarks>
public class RateLimitVisibilityGuardTests
{
    #region Every refusal is written down

    [Fact]
    public void AuthApiRejection_IsLogged()
    {
        var handler = RejectionHandler(ReadSource("Auth_API", "Program.cs"));

        handler.Should().Contain("RateLimitRejectionLog.Write(",
            "a refusal that writes nothing cannot be told from a refusal that never happened, "
            + "and every limit here is a number an operator is invited to change");
    }

    [Fact]
    public void GatewayRejection_IsLogged()
    {
        var handler = RejectionHandler(ReadSource("API_Gateway", "Program.cs"));

        handler.Should().Contain("RateLimitRejectionLog.Write(",
            "the edge refuses first, so a refusal it does not record is one the API never sees either");
    }

    /// <summary>
    /// Both hosts log through the same helper, so an operator reading the two
    /// interleaved can compare them at a glance and search them with one string.
    /// </summary>
    [Fact]
    public void BothHosts_LogThroughTheSameHelper()
    {
        var shared = ReadSource("Auth.Shared", "Diagnostics", "RateLimitRejectionLog.cs");

        shared.Should().Contain("Rate limit refused",
            "the searchable phrase is the point of sharing the helper");
        shared.Should().Contain("{Limiter}",
            "naming which allowance ran out is the whole reason this line exists");
        shared.Should().Contain("{ClientId}",
            "every limiter in both hosts partitions by client address, so the line has to say "
            + "whether one caller is spending an allowance or a crowd is sharing one");
    }

    #endregion

    #region The refusal names which allowance ran out

    /// <summary>
    /// The API can name the limiter exactly, and only because it has no global
    /// limiter: the sole limiter able to refuse is the named policy the endpoint
    /// opted into. Adding a GlobalLimiter here would silently make every logged
    /// policy name a guess.
    /// </summary>
    [Fact]
    public void AuthApi_HasNoGlobalLimiter_WhichIsWhatMakesTheLoggedPolicyNameExact()
    {
        var source = ReadSource("Auth_API", "Program.cs");

        source.Should().NotContain("options.GlobalLimiter",
            "a global bucket on an auth surface is collective punishment, and it would also make "
            + "the limiter name in every rejection log ambiguous");
    }

    /// <summary>
    /// The gateway cannot be told which limiter refused, so it works the answer
    /// out. The discriminator depends on ordering — the global lease is taken
    /// before the route policy is consulted — which is exactly the kind of
    /// reasoning that rots silently when the code around it moves.
    /// </summary>
    [Fact]
    public void Gateway_DistinguishesTheGlobalBucketFromTheRoutePolicy()
    {
        var source = ReadSource("API_Gateway", "Program.cs");

        source.Should().Contain("GlobalBucketIsEmpty(",
            "two limiters can refuse at the edge and the handler is told neither");
        source.Should().Contain("options.GlobalLimiter = globalLimiter;",
            "the handler can only interrogate the global limiter if a reference to it survives");

        RejectionHandler(source).Should().Contain("RouteModel",
            "when the endpoint carries no rate-limiting metadata the route's own configured policy "
            + "is the authoritative name");
    }

    #endregion

    #region A refused user is told to wait, not to call support

    [Fact]
    public void AuthApiRejectionBody_CarriesTheStatusTheClientClassifiesOn()
    {
        var handler = RejectionHandler(ReadSource("Auth_API", "Program.cs"));

        handler.Should().Contain("status = StatusCodes.Status429TooManyRequests",
            "the SPA reads the kind of an error from the body's status field and from nothing else; "
            + "without it a throttled user is told to contact support");
    }

    [Fact]
    public void GatewayRejectionBody_CarriesTheSameField()
    {
        var handler = RejectionHandler(ReadSource("API_Gateway", "Program.cs"));

        handler.Should().Contain("status = 429",
            "the two hosts refuse the same request for the same reason and must answer identically");
    }

    /// <summary>
    /// The reason the field is load-bearing, asserted where it actually lives.
    /// If the client ever learns to read the transport status, the C# comments
    /// above stop being true and this test is where that is noticed.
    /// </summary>
    [Fact]
    public void TheClient_StillDerivesTheErrorKindFromTheBodyStatus()
    {
        var errors = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Auth_UI", "packages", "api", "src", "errors.ts"));

        errors.Should().Contain("if (status === 429) return \"rateLimit\"",
            "429 is what turns a refusal into the 'wait a moment' message");

        After(errors, "export function getErrorStatus", 300)
            .Should().Contain("error as ApiErrorBody",
                "the status is read out of the response BODY, which is why both hosts must put it there");
    }

    #endregion

    #region Helpers

    /// <summary>The whole <c>OnRejected</c> handler of a host's Program.cs.</summary>
    private static string RejectionHandler(string source) =>
        Between(source, "options.OnRejected", "};");

    /// <summary>
    /// A window of text following a marker. Used where a brace-delimited region
    /// cannot be matched by braces: a TypeScript function that destructures on
    /// its second line closes its first <c>}</c> around the destructuring.
    /// </summary>
    private static string After(string source, string marker, int length)
    {
        var from = source.IndexOf(marker, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, "'{0}' must exist in the source", marker);

        return source[from..Math.Min(source.Length, from + length)];
    }

    /// <summary>The text between the first occurrence of two markers.</summary>
    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, "'{0}' must exist in the source", start);

        from += start.Length;
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        to.Should().BeGreaterThan(from, "'{0}' must close with '{1}'", start, end);

        return source[from..to];
    }

    private static string ReadSource(params string[] relativeParts)
        => File.ReadAllText(Path.Combine(SolutionDirectory(), Path.Combine(relativeParts)));

    /// <summary>The folder holding Auth.sln.</summary>
    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must be able to find Auth.sln above their output folder");
        return directory!.FullName;
    }

    /// <summary>The repository root, which holds both Auth and Auth_UI.</summary>
    private static string RepositoryRoot() => Directory.GetParent(SolutionDirectory())!.FullName;

    #endregion
}
