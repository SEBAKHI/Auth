namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// Both hosts wire the error contract (ADR 0001) in the same place, and neither brings back a
/// writer of its own. Read from source, like the other Program.cs guards: the gateway cannot be
/// referenced from this project, and an order in a pipeline is exactly what a unit test cannot see.
/// </summary>
public class ErrorContractWiringGuardTests
{
    [Theory]
    [InlineData("Auth_API", "AddApiErrorContract()")]
    [InlineData("API_Gateway", "AddErrorContract()")]
    public void Host_RegistersTheErrorContract(string host, string registration)
    {
        Assert.Contains(registration, Program(host), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Auth_API", "app.UseAuthLocalization();", "app.UseErrorContract();")]
    [InlineData("Auth_API", "app.UseErrorContract();", "app.UseMiddleware<GatewayTokenValidationMiddleware>();")]
    [InlineData("Auth_API", "app.UseErrorContract();", "app.UseAuthentication();")]
    [InlineData("Auth_API", "app.UseErrorContract();", "app.UseRateLimiter();")]
    [InlineData("API_Gateway", "app.UseAuthLocalization();", "app.UseErrorContract();")]
    [InlineData("API_Gateway", "app.UseErrorContract();", "app.UseRateLimiter();")]
    [InlineData("API_Gateway", "app.UseErrorContract();", "app.MapReverseProxy();")]
    public void Pipeline_OrdersTheErrorContract(string host, string first, string second)
    {
        // Localization first, so detail is in the request's language; then the contract, ahead
        // of every component whose empty 401, 403, 429, 404 or 502 it has to write.
        var source = Program(host);
        var firstAt = source.IndexOf(first, StringComparison.Ordinal);
        var secondAt = source.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstAt > 0, $"{host} must call {first}");
        Assert.True(secondAt > firstAt, $"{host} must call {first} before {second}");
    }

    [Theory]
    [InlineData("Auth_API")]
    [InlineData("API_Gateway")]
    public void Host_WritesNoErrorBodyOfItsOwn(string host)
    {
        var source = Program(host);

        Assert.DoesNotContain("WriteAsJsonAsync(new", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionMiddleware", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionHandlingMiddleware", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The patterns every hand-written error body in these hosts used (ADR 0001 removed them
    /// all): a ProblemDetails built by hand, an anonymous <c>{ error }</c> object, a JSON body
    /// written straight to the response, MVC's dictionary-shaped validation problem, and an
    /// exception middleware of its own. Anything that refuses a request sets a status and
    /// records its code; the pipeline writes the body.
    /// </summary>
    [Theory]
    [InlineData("new ProblemDetails")]
    [InlineData("new ValidationProblemDetails")]
    [InlineData("new HttpValidationProblemDetails")]
    [InlineData("new { error")]
    [InlineData("WriteAsJsonAsync(new")]
    [InlineData("ValidationProblem(")]
    [InlineData("ExceptionMiddleware")]
    public void NoHostSource_BuildsAnErrorBodyByHand(string pattern)
    {
        var offenders = new[] { "Auth_API", "API_Gateway" }
            .SelectMany(host => Directory.EnumerateFiles(
                Path.Combine(SolutionDirectory(), host), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => File.ReadAllText(file).Contains(pattern, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(SolutionDirectory(), file))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string Program(string host) =>
        File.ReadAllText(Path.Combine(SolutionDirectory(), host, "Program.cs"));

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln was not found above the test output folder.");
    }
}
