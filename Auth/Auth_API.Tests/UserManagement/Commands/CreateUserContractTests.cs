using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Application.Features.Users.CreateUser;
using Auth_API.Modules.UserManagement.Contracts;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.UserManagement.Commands;

/// <summary>
/// OI-109 T1 and T2: <c>POST api/v1/users</c> carries no role. A role given at
/// creation skipped <c>PermissionGrantGuard</c> and <c>PlatformGrantFactorGuard</c>,
/// so <c>users:create</c> alone could mint a platform administrator; roles are now
/// given only through <c>POST api/v1/users/{id}/roles</c>.
/// </summary>
public class CreateUserContractTests
{
    /// <summary>T1. The controller maps the request onto the command, so this also compile-checks that mapping.</summary>
    [Theory]
    [InlineData(typeof(CreateUserRequest))]
    [InlineData(typeof(CreateUserCommand))]
    public void CreateContract_HasNoRoleMember(Type contract)
    {
        const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var shape = contract.GetProperties(members).Select(property => (property.Name, Type: property.PropertyType))
            .Concat(contract.GetConstructors(members).SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => (Name: parameter.Name ?? "", Type: parameter.ParameterType)))
            .ToList();

        shape.Should().Contain(member => member.Name == "Email", "the scan reads the contract it names");
        shape.Where(member => member.Name.Contains("role", StringComparison.OrdinalIgnoreCase) || IsGuidCollection(member.Type))
            .Select(member => member.Name)
            .Should().BeEmpty("a role given at creation skips both grant guards; assign it with POST api/v1/users/{id}/roles");
    }

    private static bool IsGuidCollection(Type type) =>
        typeof(IEnumerable<Guid>).IsAssignableFrom(type) || typeof(IEnumerable<Guid?>).IsAssignableFrom(type);

    /// <summary>
    /// T2. What an old client sees: a body that still names <c>roleIds</c> is accepted
    /// (201) and the account gets no role, because the API ignores unknown members.
    /// The options mirror <c>Auth_API/Program.cs</c> by hand (no WebApplicationFactory
    /// in this suite); the next test pins that mirror.
    /// </summary>
    [Fact]
    public void OldClientBody_WithRoleIds_BindsAndCarriesNoRole()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        const string body = """
            {"email":"new@example.com","password":"ValidPass1!","firstName":"New","lastName":"User",
             "roleIds":["10000000-0000-0000-0000-000000000001"]}
            """;

        var request = JsonSerializer.Deserialize<CreateUserRequest>(body, options);

        request.Should().NotBeNull();
        request!.Email.Should().Be("new@example.com");
        request.LastName.Should().Be("User");
        JsonSerializer.Serialize(request, options).Should().NotContainEquivalentOf("role");
    }

    [Fact]
    public void ApiJsonOptions_StillIgnoreUnknownMembers()
    {
        var sources = ApiSourceScan.ProductionSources().ToList();
        sources.Should().NotBeEmpty();

        // Rejecting unknown members would turn an old client's 201 into a 400:
        // a decision to take on purpose, and T2 above to rewrite with it.
        sources.Where(s => s.Source.Contains("UnmappedMemberHandling", StringComparison.Ordinal))
            .Select(s => Path.GetFileName(s.File))
            .Should().BeEmpty("T2 assumes the API ignores unknown JSON members");

        var program = sources.Single(s => s.File.EndsWith(
            Path.Combine("Auth_API", "Program.cs"), StringComparison.Ordinal)).Source;
        program.Should().Contain("options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;")
            .And.Contain("options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());");
    }
}
