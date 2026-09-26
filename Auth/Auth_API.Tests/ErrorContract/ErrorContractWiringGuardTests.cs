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
