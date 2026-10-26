using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Platform.GetPlatformBranding;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// OI-215 T5 (V7): sign-up without a password needs email sending on. The save is
/// refused from either section, and at run time the password switch is honoured only
/// while email is on, so no account is created that could not sign in. The sign-in
/// page reads the effective options from the branding answer.
/// </summary>
/// <remarks>Deliberate break: drop the email check from <c>SignUpRequirements.From</c>.</remarks>
public class PasswordlessSignUpEmailRuleTests
{
    private static List<Error> Validate(
        string sectionKey, (string Path, bool Value)[] payload, Dictionary<string, string?> live, bool reset = false)
    {
        var section = SystemSettingsRegistry.TryGet(sectionKey)!;
        var values = payload
            .Select(entry => new KeyValuePair<string, JsonElement>(
                entry.Path, JsonDocument.Parse(entry.Value ? "true" : "false").RootElement.Clone()))
            .ToList();
        var errors = new List<Error>();

        SystemSettingsValueValidator.ValidateSectionRules(
            section,
            reset ? [] : values,
            errors,
            key => live.TryGetValue(key, out var value) ? value : null,
            requestOrigin: null,
            baselineValue: key => live.TryGetValue(key, out var value) ? value : null,
            liveValue: key => live.TryGetValue(key, out var value) ? value : null);

        return errors;
    }

    [Fact]
    public void Registration_SavingThePasswordSwitchOffWhileEmailIsOff_IsRefused()
    {
        var errors = Validate("Registration", [("PasswordAtSignUp", false)], new() { ["Email:Enabled"] = "false" });

        errors.Should().ContainSingle();
        errors[0].Code.Should().Be("SystemSettings.PasswordlessSignUpRequiresEmail");
        ((object[])errors[0].Metadata!["args"])[0].Should().Be("PasswordAtSignUp");
    }

    [Fact]
    public void Email_SwitchingSendingOffWhileSignUpIsPasswordless_IsRefused()
    {
        var errors = Validate("Email", [("Enabled", false)], new()
        {
            ["Registration:PasswordAtSignUp"] = "false",
            ["Email:FrontendBaseUrl"] = "https://accounts.example.com",
        });

        errors.Should().ContainSingle().Which.Code.Should().Be("SystemSettings.PasswordlessSignUpRequiresEmail");
        ((object[])errors[0].Metadata!["args"])[0].Should().Be("Enabled");
    }

    [Theory]
    [InlineData("true", false)]
    [InlineData("false", true)]
    [InlineData(null, false)]
    public void Registration_SavingAnyOtherCombination_IsAccepted(string? emailEnabled, bool passwordAtSignUp)
    {
        // Email on with the switch off; the switch on with email off; an absent email
        // key runs with its default (on).
        var live = new Dictionary<string, string?> { ["Email:Enabled"] = emailEnabled };

        Validate("Registration", [("PasswordAtSignUp", passwordAtSignUp)], live).Should().BeEmpty();
    }

    [Fact]
    public void Registration_ResetToABaselineThatLeavesThePasswordOn_IsAccepted()
    {
        Validate("Registration", [], new() { ["Email:Enabled"] = "false" }, reset: true).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void SignUpRequirements_HonourThePasswordSwitchOnlyWhileEmailIsOn(
        bool passwordAtSignUp, bool emailEnabled, bool passwordRequired)
    {
        var requirements = SignUpRequirements.From(
            new RegistrationSettings { PasswordAtSignUp = passwordAtSignUp, NameAtSignUp = false },
            new EmailSettings { Enabled = emailEnabled });

        requirements.PasswordRequired.Should().Be(passwordRequired);
        requirements.NameRequired.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, true, true, "password", true)]
    [InlineData(false, true, true, "code", false)]
    [InlineData(false, false, false, "password", true)]
    public async Task Branding_CarriesTheEffectiveSignInOptions(
        bool passwordAtSignUp, bool emailEnabled, bool codeOffered, string primary, bool passwordRequired)
    {
        var repository = new Mock<IPlatformSettingsRepository>();
        repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PlatformSettings?)null);
        var handler = new GetPlatformBrandingQueryHandler(
            repository.Object,
            new Mock<IImageUrlComposer>().Object,
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            TestHelpers.CreateOptions(new RegistrationSettings
            {
                PasswordAtSignUp = passwordAtSignUp, NameAtSignUp = false, AllowSelfRegistration = false
            }),
            TestHelpers.CreateOptions(new EmailSettings { Enabled = emailEnabled }));

        var branding = (await handler.Handle(new GetPlatformBrandingQuery(), CancellationToken.None)).Value;

        branding.CodeSignInOffered.Should().Be(codeOffered);
        branding.PrimaryMethod.Should().Be(primary, "the code is primary exactly when sign-up has no password (D-215-6 (a))");
        branding.PasswordAtSignUp.Should().Be(passwordRequired);
        branding.NameAtSignUp.Should().BeFalse();
        branding.SelfRegistrationOpen.Should().BeFalse();
        new[] { SignInMethods.Code, SignInMethods.Password }.Should().Contain(branding.PrimaryMethod);
    }
}
