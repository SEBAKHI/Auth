using Auth.Application.Configuration;
using Auth.Application.Features.SystemSettings.UpdateSystemSettings;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Administration;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// OI-165: who decides whether a sign-in outlives the browser. One setting in the
/// console's IdentityProvider section, hot, the person by default; one resolution
/// on the settings class, which every consumer reads, and which reads a value
/// outside the enum as the default.
/// </summary>
public class StaySignedInModeSettingsTests
{
    private static SettingFieldDefinition Field() =>
        SystemSettingsRegistry.Sections.Single(section => section.Key == "IdentityProvider")
            .Fields.Should().ContainSingle(field => field.Path == "StaySignedInMode").Subject;

    [Theory]
    [InlineData(StaySignedInMode.PersonChooses, false, false)]
    [InlineData(StaySignedInMode.PersonChooses, true, true)]
    [InlineData(StaySignedInMode.AlwaysBrowserSession, false, false)]
    [InlineData(StaySignedInMode.AlwaysBrowserSession, true, false)]
    [InlineData(StaySignedInMode.AlwaysStaySignedIn, false, true)]
    [InlineData(StaySignedInMode.AlwaysStaySignedIn, true, true)]
    public void SignInIsPersistent_FollowsTheMode(StaySignedInMode mode, bool personTicked, bool expected)
    {
        new IdentityProviderSettings { StaySignedInMode = mode }.SignInIsPersistent(personTicked).Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignInIsPersistent_WithAnUndefinedMode_ReadsAsThePersonsChoice(bool personTicked)
    {
        // A settings file or an environment variable may bind 7. A typo falls back
        // to the default behaviour, never to either of the administrator's choices.
        var settings = new IdentityProviderSettings { StaySignedInMode = (StaySignedInMode)7 };

        settings.SignInIsPersistent(personTicked).Should().Be(personTicked);
        settings.StaySignedInModeInForce.Should().Be(StaySignedInMode.PersonChooses);
        settings.StaySignedInOffered.Should().BeTrue();
    }

    [Theory]
    [InlineData(StaySignedInMode.PersonChooses, true)]
    [InlineData(StaySignedInMode.AlwaysBrowserSession, false)]
    [InlineData(StaySignedInMode.AlwaysStaySignedIn, false)]
    public void StaySignedInOffered_OnlyWhileThePersonDecides(StaySignedInMode mode, bool expected)
    {
        new IdentityProviderSettings { StaySignedInMode = mode }.StaySignedInOffered.Should().Be(expected);
    }

    [Fact]
    public void TheDefault_IsThePersonsChoice()
    {
        new IdentityProviderSettings().StaySignedInMode.Should().Be(StaySignedInMode.PersonChooses,
            "with the default nothing changes for any deployment");
    }

    [Fact]
    public void TheField_IsAnEditableHotEnum_WithTheThreeValuesInOrder()
    {
        var field = Field();

        field.Kind.Should().Be(SettingKind.Enum);
        field.Editable.Should().BeTrue();
        field.RestartRequired.Should().BeFalse("read through IOptionsSnapshot when a session is minted");
        field.AllowedValues.Should().Equal("PersonChooses", "AlwaysBrowserSession", "AlwaysStaySignedIn");
        field.DefaultValue.Should().Be("PersonChooses");
        field.AllowedValues.Should().Equal(Enum.GetNames<StaySignedInMode>(),
            "every value the console offers binds to the enum, and the enum has no value the console hides");
    }

    [Fact]
    public async Task AConsoleSaveOfAnUnknownValue_IsRefused()
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration(
            ("IdentityProvider:AccountsBaseUrl", "https://accounts.example.com"));
        var rows = new Mock<ISystemSettingsRepository>();
        var handler = new UpdateSystemSettingsCommandHandler(
            rows.Object, new Mock<IUserRepository>().Object, configuration,
            SystemSettingsTestSupport.SnapshotOf(configuration), new Mock<ISystemSettingsReloader>().Object,
            new Mock<IPublisher>().Object, new Mock<IIdpSessionIdleLimitChange>().Object,
            NullLogger<UpdateSystemSettingsCommandHandler>.Instance);

        var result = await handler.Handle(
            new UpdateSystemSettingsCommand(
                "IdentityProvider", SystemSettingsTestSupport.Json("""{"StaySignedInMode":"Always"}"""), null, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.InvalidFieldValue");
        rows.Verify(
            r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}
