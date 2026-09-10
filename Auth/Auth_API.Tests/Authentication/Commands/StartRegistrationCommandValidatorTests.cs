using Auth.Application.Features.Authentication.StartRegistration;
using Auth.Application.Features.Authentication.VerifyRegistration;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The two registration commands' validators. The address bound matters more
/// here than elsewhere: this is an anonymous endpoint at the registration
/// limit, and an address one character too long used to pass every validator
/// and fail inside the INSERT as a 500.
/// </summary>
public class StartRegistrationCommandValidatorTests
{
    private readonly StartRegistrationCommandValidator _start = new();
    private readonly VerifyRegistrationCommandValidator _verify = new();

    [Fact]
    public void AnAddressAtTheColumnBound_IsAccepted()
    {
        var result = _start.Validate(new StartRegistrationCommand(Address(254), "en", null, null));

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    public void AnAddressPastTheColumnBound_IsRefusedBeforeItReachesTheDatabase(int length)
    {
        var result = _start.Validate(new StartRegistrationCommand(Address(length), "en", null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.ErrorMessage).Should().Contain("Validation.Email.MaxLength",
            "the address columns are NVARCHAR(255); past that the INSERT fails, not the validator");
    }

    [Fact]
    public void AnUnsupportedLanguage_IsRefused_AndNoneIsFine()
    {
        _start.Validate(new StartRegistrationCommand("jane@one.example", "xx", null, null)).IsValid.Should().BeFalse();
        _start.Validate(new StartRegistrationCommand("jane@one.example", null, null, null)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("")]
    public void AMalformedCode_IsRefused(string otp)
    {
        _verify.Validate(new VerifyRegistrationCommand("handle-1", otp)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ASixDigitCode_IsAccepted()
    {
        _verify.Validate(new VerifyRegistrationCommand("handle-1", "123456")).IsValid.Should().BeTrue();
    }

    /// <summary>An address of exactly the requested length that the format rule accepts.</summary>
    private static string Address(int length)
    {
        const string domain = "@example.com";
        return new string('a', length - domain.Length) + domain;
    }
}
