using Auth.Application.Configuration;
using Auth.Application.Validators;
using ErrorOr;
using Microsoft.Extensions.Options;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Validators;

public class PasswordValidatorTests
{
    private static PasswordValidator CreateValidator(
        int minimumLength = 12,
        bool requireUppercase = true,
        bool requireLowercase = true,
        bool requireDigit = true,
        bool requireSpecialCharacter = true)
    {
        var settings = TestHelpers.CreateOptions(new PasswordSettings
        {
            MinimumLength = minimumLength,
            RequireUppercase = requireUppercase,
            RequireLowercase = requireLowercase,
            RequireDigit = requireDigit,
            RequireSpecialCharacter = requireSpecialCharacter
        });
        return new PasswordValidator(settings);
    }

    [Fact]
    public void Validate_ValidPassword_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate("StrongP@ss1234");

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyPassword_ReturnsRequired()
    {
        var validator = CreateValidator();

        var result = validator.Validate("");

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Password.Required");
    }

    [Fact]
    public void Validate_NullPassword_ReturnsRequired()
    {
        var validator = CreateValidator();

        var result = validator.Validate(null!);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Password.Required");
    }

    [Fact]
    public void Validate_TooShort_ReturnsTooShort()
    {
        var validator = CreateValidator(minimumLength: 12);

        var result = validator.Validate("Short1@a");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.TooShort");
    }

    [Fact]
    public void Validate_ExactMinLength_Succeeds()
    {
        var validator = CreateValidator(minimumLength: 8);

        var result = validator.Validate("Str0ng@!");

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public void Validate_NoUppercase_ReturnsRequiresUppercase()
    {
        var validator = CreateValidator(minimumLength: 8);

        var result = validator.Validate("lowercase1@!");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.RequiresUppercase");
    }

    [Fact]
    public void Validate_NoLowercase_ReturnsRequiresLowercase()
    {
        var validator = CreateValidator(minimumLength: 8);

        var result = validator.Validate("UPPERCASE1@!");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.RequiresLowercase");
    }

    [Fact]
    public void Validate_NoDigit_ReturnsRequiresDigit()
    {
        var validator = CreateValidator(minimumLength: 8);

        var result = validator.Validate("NoDigits@!");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.RequiresDigit");
    }

    [Fact]
    public void Validate_NoSpecialChar_ReturnsRequiresSpecialCharacter()
    {
        var validator = CreateValidator(minimumLength: 8);

        var result = validator.Validate("NoSpecial1Aa");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.RequiresSpecialCharacter");
    }

    [Fact]
    public void Validate_CommonPattern_ReturnsCommonPattern()
    {
        var validator = CreateValidator(minimumLength: 8);

        var result = validator.Validate("Password1@!");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.CommonPattern");
    }

    [Fact]
    public void Validate_CommonPatternQwerty_ReturnsCommonPattern()
    {
        var validator = CreateValidator(minimumLength: 8);

        var result = validator.Validate("Qwerty1@!x");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.CommonPattern");
    }

    [Fact]
    public void Validate_MultipleFailures_ReturnsAllErrors()
    {
        var validator = CreateValidator(minimumLength: 20);

        // "abc" is too short, no uppercase, no digit, no special char
        var result = validator.Validate("abc");

        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == "Password.TooShort");
        result.Errors.Should().Contain(e => e.Code == "Password.RequiresUppercase");
        result.Errors.Should().Contain(e => e.Code == "Password.RequiresDigit");
        result.Errors.Should().Contain(e => e.Code == "Password.RequiresSpecialCharacter");
    }

    [Fact]
    public void Validate_WithAllRequirementsDisabled_AcceptsSimplePassword()
    {
        var validator = CreateValidator(
            minimumLength: 1,
            requireUppercase: false,
            requireLowercase: false,
            requireDigit: false,
            requireSpecialCharacter: false);

        var result = validator.Validate("a");

        result.IsError.Should().BeFalse();
    }
}
