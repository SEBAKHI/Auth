using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Auth.Application.Features.Authentication.ChangePassword;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Authentication.ResetPassword;
using Auth_API.Common;

namespace Auth_API.Tests.Validators;

/// <summary>
/// Requiredness, format and range are validator rules with catalog codes, never DataAnnotations
/// on a request (ADR 0001): an attribute fails in model binding, where the failure carries only
/// Http.BadRequest, and a C# <c>required</c> member fails in the JSON reader the same way. These
/// cover the rules that replaced the attributes, and keep the attributes from coming back.
/// </summary>
public class RequestRuleCoverageTests
{
    [Fact]
    public void RequestContracts_CarryNoDataAnnotationsAndNoRequiredMembers()
    {
        var offenders = typeof(ApiController).Assembly.GetTypes()
            .Where(type => type.Namespace?.EndsWith(".Contracts", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.DeclaringType == type)
                .Where(property => property.GetCustomAttributes<ValidationAttribute>().Any()
                    || property.GetCustomAttributes<RequiredMemberAttribute>().Any())
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Validate_ResetPasswordWithDifferentConfirmation_ReturnsConfirmationMismatch()
    {
        var result = new ResetPasswordCommandValidator().Validate(
            new ResetPasswordCommand("token", "NewPass1!", ConfirmNewPassword: "NewPass2!"));

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Password.ConfirmationMismatch", failure.ErrorCode);
        Assert.Equal(nameof(ResetPasswordCommand.ConfirmNewPassword), failure.PropertyName);
    }

    [Theory]
    [InlineData("NewPass1!")] // typed the same twice
    [InlineData(null)]        // a caller that carries no confirmation
    public void Validate_ResetPasswordWithMatchingOrNoConfirmation_IsValid(string? confirmation)
    {
        var result = new ResetPasswordCommandValidator().Validate(
            new ResetPasswordCommand("token", "NewPass1!", ConfirmNewPassword: confirmation));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ChangePasswordWithMissingConfirmation_ReturnsConfirmationMismatch()
    {
        // The request's default for an omitted member is empty, which matches no password.
        var result = new ChangePasswordCommandValidator().Validate(new ChangePasswordCommand(
            Guid.NewGuid(), "OldPass1!", "NewPass1!", ConfirmNewPassword: string.Empty));

        Assert.Equal("Password.ConfirmationMismatch", Assert.Single(result.Errors).ErrorCode);
    }

    [Theory]
    [InlineData(nameof(ExternalLoginCommand.GivenName), "ExternalAuth.GivenNameTooLong")]
    [InlineData(nameof(ExternalLoginCommand.FamilyName), "ExternalAuth.FamilyNameTooLong")]
    public void Validate_ExternalLoginWithNamePastItsColumn_ReturnsItsCode(string member, string code)
    {
        var name = new string('n', 101);
        var command = member == nameof(ExternalLoginCommand.GivenName)
            ? ExternalLogin() with { GivenName = name }
            : ExternalLogin() with { FamilyName = name };

        var failure = Assert.Single(new ExternalLoginCommandValidator().Validate(command).Errors);

        Assert.Equal(code, failure.ErrorCode);
        Assert.Equal(member, failure.PropertyName);
    }

    [Fact]
    public void Validate_ExternalLoginWithOverlongProviderAndCode_ReturnsTheirCodes()
    {
        var command = ExternalLogin() with
        {
            Provider = new string('p', 51),
            AuthorizationCode = new string('c', 2001),
        };

        var codes = new ExternalLoginCommandValidator().Validate(command).Errors.Select(e => e.ErrorCode);

        Assert.Equal(["ExternalAuth.ProviderTooLong", "ExternalAuth.AuthorizationCodeTooLong"], codes);
    }

    [Fact]
    public void Validate_ExternalLoginWithNamesAtTheirColumn_IsValid()
    {
        var command = ExternalLogin() with
        {
            GivenName = new string('n', 100),
            FamilyName = new string('n', 100),
            AuthorizationCode = new string('c', 2000),
        };

        Assert.True(new ExternalLoginCommandValidator().Validate(command).IsValid);
    }

    private static ExternalLoginCommand ExternalLogin() => new(
        Provider: "google",
        IdToken: "id-token",
        Nonce: "nonce");
}
