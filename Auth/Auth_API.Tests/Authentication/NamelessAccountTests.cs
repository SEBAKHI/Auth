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

    private static UpdateUserCommand AdministratorEdit(string firstName, string lastName) =>
        new(Guid.NewGuid(), firstName, lastName) { ModifiedBy = Guid.NewGuid() };
}
