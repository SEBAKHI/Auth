using Auth.Application.Features.Users.UpdateProfile;
using Auth.Application.Features.Users.UpdateUser;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-215 T9 (V10): an account may have no name. Its access token carries no name
/// claims rather than a blank or a space, and its owner and an administrator can save
/// its profile with the names empty.
/// </summary>
/// <remarks>Deliberate break: add <c>name</c>, <c>given_name</c> and <c>family_name</c> unconditionally.</remarks>
public class NamelessAccountTests
{
    [Fact]
    public void TheAccessToken_OfANamelessAccount_CarriesNoNameClaims()
    {
        using var tokens = new UserInfoTokens();
        var user = TestHelpers.CreateUser(email: "jane@one.example", firstName: "", lastName: "");

        var token = new JsonWebTokenHandler().ReadJsonWebToken(tokens.ForPlatform(user));

        token.Claims.Select(claim => claim.Type).Should().NotContain(
            [JwtClaimNames.Name, JwtClaimNames.GivenName, JwtClaimNames.FamilyName],
            "a blank name claim is a value a relying party would show");
        token.GetClaim(JwtClaimNames.Email).Value.Should().Be("jane@one.example");
    }

    [Fact]
    public void TheAccessToken_OfAFirstNameOnly_CarriesItWithoutATrailingSpace()
    {
        using var tokens = new UserInfoTokens();
        var user = TestHelpers.CreateUser(firstName: "Jane", lastName: "");

        var token = new JsonWebTokenHandler().ReadJsonWebToken(tokens.ForPlatform(user));

        token.GetClaim(JwtClaimNames.Name).Value.Should().Be("Jane");
        token.GetClaim(JwtClaimNames.GivenName).Value.Should().Be("Jane");
        token.Claims.Should().NotContain(claim => claim.Type == JwtClaimNames.FamilyName);
    }

    [Fact]
    public void TheAccessToken_OfANamedAccount_IsUnchanged()
    {
        using var tokens = new UserInfoTokens();
        var user = TestHelpers.CreateUser(firstName: "Jane", lastName: "Doe");

        var token = new JsonWebTokenHandler().ReadJsonWebToken(tokens.ForPlatform(user));

        token.GetClaim(JwtClaimNames.Name).Value.Should().Be("Jane Doe");
        token.GetClaim(JwtClaimNames.GivenName).Value.Should().Be("Jane");
        token.GetClaim(JwtClaimNames.FamilyName).Value.Should().Be("Doe");
    }

    [Fact]
    public void TheProfileEdit_AcceptsEmptyNames_AndKeepsTheLengthLimit()
    {
        var validator = new UpdateProfileCommandValidator();

        validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), FirstName: "", LastName: "")).IsValid.Should().BeTrue(
            "an account without a name must be able to save its own profile");
        validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), FirstName: new string('a', 101))).Errors
            .Should().ContainSingle(e => e.ErrorCode == "User.FirstNameTooLong");
    }

    [Fact]
    public void TheAdministratorsEdit_AcceptsEmptyNames_AndKeepsTheLengthLimit()
    {
        var validator = new UpdateUserCommandValidator();

        validator.Validate(AdministratorEdit("", "")).IsValid.Should().BeTrue();
        validator.Validate(AdministratorEdit("", new string('b', 101))).Errors
            .Should().ContainSingle(e => e.ErrorCode == "User.LastNameTooLong");
    }

    [Fact]
    public void TheAdministratorsEdit_StillRefusesOmittedNames_SoAPartialBodyBlanksNothing()
    {
        // REVIEW 110 F11: a full update; null is "not sent", the empty string clears.
        var result = new UpdateUserCommandValidator().Validate(AdministratorEdit(null!, null!));

        result.Errors.Select(e => e.ErrorCode).Should().BeEquivalentTo(["User.FirstNameRequired", "User.LastNameRequired"]);
    }

    /// <summary>
    /// REVIEW 110 F4: one server rule for the name to show (the name, or the address),
    /// <c>User.NameToShow</c> and its read-model forms. A hand-made concatenation of the
    /// two parts gives "" for a nameless person, which was mailed ("<blank> has invited
    /// you") and shown. None is left in Auth.Application; the application directory,
    /// whose published contract is null for "no name", reads <c>User.DisplayNameFrom</c>.
    /// </summary>
    [Fact]
    public void NoHandler_BuildsANameFromItsTwoParts_ByHand()
    {
        var concatenation = new System.Text.RegularExpressions.Regex(
            @"\$""\{[A-Za-z_.?]*FirstName\} \{[A-Za-z_.?]*LastName\}""");
        var root = Path.Combine(SolutionDirectory(), "Auth.Application");

        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => concatenation.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Should().NotBeEmpty("the guard must read real files");
        offenders.Should().BeEmpty("a nameless person's name is their address (User.NameToShow), never \"\"");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }

    private static UpdateUserCommand AdministratorEdit(string firstName, string lastName) =>
        new(Guid.NewGuid(), firstName, lastName) { ModifiedBy = Guid.NewGuid() };
}
