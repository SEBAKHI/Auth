using Auth_API.Common;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// Guards the placeholder check on the resolved AuthDb connection string.
/// </summary>
/// <remarks>
/// appsettings.json ships <c>"AuthDb": "ConnectionStrings__AuthDb"</c> as a
/// reminder of the environment variable meant to override it. Because that
/// literal is a non-empty string, the <c>?? throw</c> on
/// <c>GetConnectionString("AuthDb")</c> never fires and the placeholder reaches
/// the SQL driver, which reports a keyword parse error naming an argument the
/// operator never wrote.
/// </remarks>
public class ConnectionStringGuardTests
{
    [Fact]
    public void EnsureResolved_Placeholder_Throws()
    {
        var act = () => ConnectionStringGuard.EnsureResolved("ConnectionStrings__AuthDb");

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// The message has to name both causes. Once the connection string can live
    /// in the secrets file, "still the placeholder" also means "the secrets file
    /// could not be read" — which on a freshly migrated server is a missing
    /// certificate, not a configuration mistake.
    /// </summary>
    [Fact]
    public void EnsureResolved_Placeholder_MessageNamesBothCausesAndTheEscapeHatch()
    {
        var act = () => ConnectionStringGuard.EnsureResolved("ConnectionStrings__AuthDb");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
                .Contain("secrets file")
                .And.Contain("ConnectionStrings__AuthDb")
                .And.Contain("AUTH_IGNORE_SECRET_CONNECTIONSTRING");
    }

    [Theory]
    [InlineData("  ConnectionStrings__AuthDb  ")]
    public void EnsureResolved_PlaceholderWithSurroundingWhitespace_StillThrows(string value)
    {
        var act = () => ConnectionStringGuard.EnsureResolved(value);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Server=localhost;Database=AuthDb;Integrated Security=true")]
    [InlineData("Server=tcp:db.example.com,1433;Initial Catalog=AuthDb;User Id=app;Password=ConnectionStrings__AuthDb")]
    [InlineData(null)]
    [InlineData("")]
    public void EnsureResolved_RealOrAbsentValue_DoesNotThrow(string? value)
    {
        // The second case matters: matching is exact, so a real connection string
        // that merely CONTAINS the placeholder text must pass. A "looks nothing
        // like a connection string" heuristic would eventually reject a valid one.
        // Null/empty are left to the caller's own null check.
        var act = () => ConnectionStringGuard.EnsureResolved(value);

        act.Should().NotThrow();
    }
}
