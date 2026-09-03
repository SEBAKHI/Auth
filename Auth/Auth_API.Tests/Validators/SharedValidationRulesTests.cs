using Auth.Application.Features.Authentication.Register;
using Auth.Application.Features.Authentication.ResetPassword;
using Auth.Application.Features.Users.UpdateProfile;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Validators;

public class SharedValidationRulesTests
{
    private readonly UpdateProfileCommandValidator _validator = new();

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Riyadh")]
    [InlineData("Etc/UTC")]
    [InlineData("Europe/Paris")]
    public void TimeZone_IanaIdentifiers_AreValid(string timeZone)
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), TimeZone: timeZone));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Arab Standard Time")] // Windows id — rejected to keep stored values IANA
    [InlineData("Not/AZone")]
    [InlineData("Riyadh")]
    public void TimeZone_NonIanaIdentifiers_AreRejected(string timeZone)
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), TimeZone: timeZone));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Validation.TimeZone.Invalid");
    }

    [Fact]
    public void TimeZone_Null_IsValid()
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("tr")]
    [InlineData("fr")]
    [InlineData("zh")]
    [InlineData("ur")]
    [InlineData("fa")]
    public void PreferredLanguage_SupportedCultures_AreValid(string language)
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), PreferredLanguage: language));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("de")]
    [InlineData("english")]
    [InlineData("")]
    public void PreferredLanguage_UnsupportedValues_AreRejected(string language)
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), PreferredLanguage: language));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Validation.PreferredLanguage.NotSupported");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("system")]
    [InlineData("DARK")] // case-insensitive, like preferred language
    public void Theme_SupportedValues_AreValid(string theme)
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), Theme: theme));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("auto")]
    [InlineData("")]
    public void Theme_UnsupportedValues_AreRejected(string theme)
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid(), Theme: theme));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Validation.Theme.NotSupported");
    }

    [Fact]
    public void Theme_Null_IsValid()
    {
        var result = _validator.Validate(new UpdateProfileCommand(Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Password_AtTheCeiling_IsAccepted()
    {
        var result = new RegisterCommandValidator().Validate(
            new RegisterCommand("john@example.com", new string('a', PasswordLimits.MaxLength), "John", "Doe"));

        result.Errors.Should().NotContain(e => e.ErrorMessage == "Validation.Password.MaxLength");
    }

    [Fact]
    public void Password_OverTheCeiling_IsRejected_WhetherSetOrPresented()
    {
        var oversized = new string('a', PasswordLimits.MaxLength + 1);

        new RegisterCommandValidator()
            .Validate(new RegisterCommand("john@example.com", oversized, "John", "Doe"))
            .Errors.Should().Contain(e => e.ErrorMessage == "Validation.Password.MaxLength");
        new ResetPasswordCommandValidator()
            .Validate(new ResetPasswordCommand("token", oversized))
            .Errors.Should().Contain(e => e.ErrorMessage == "Validation.Password.MaxLength");
    }
}
