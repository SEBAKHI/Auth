using Auth.Application.Configuration;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// S23, AM11 (11-1): the application raises its two-step change itself, only when the
/// value changes, and the setting that decides what the requirement does reads safely.
/// </summary>
/// <remarks>
/// Deliberate break: raise the event on every update; the "unchanged" test turns red.
/// </remarks>
public class ApplicationTwoFactorRequirementTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static void Save(ApplicationEntity application, bool requireTwoFactor) =>
        application.Update(
            "CRM", null, null, null, null, null, false, requireTwoFactor, false, 60, 5,
            ApplicationAccessMode.Restricted, Admin);

    [Fact]
    public void Update_SwitchingTheRequirementOn_RaisesOneChange()
    {
        var application = ApplicationEntity.Create("crm", "CRM", null, null, Admin);

        Save(application, requireTwoFactor: true);

        var raised = application.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<ApplicationTwoFactorRequirementChangedEvent>().Subject;
        raised.ApplicationId.Should().Be(application.Id);
        raised.ApplicationCode.Should().Be("CRM");
        raised.RequireTwoFactor.Should().BeTrue();
        raised.TriggeredBy.Should().Be(Admin);
        raised.OccurredAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Update_SwitchingTheRequirementOff_RaisesTheChange()
    {
        var application = ApplicationEntity.Create("crm", "CRM", null, null, Admin);
        Save(application, requireTwoFactor: true);
        application.ClearDomainEvents();

        Save(application, requireTwoFactor: false);

        application.DomainEvents.OfType<ApplicationTwoFactorRequirementChangedEvent>()
            .Should().ContainSingle().Which.RequireTwoFactor.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Update_TheSameValue_RaisesNothing(bool requireTwoFactor)
    {
        var application = ApplicationEntity.Create("crm", "CRM", null, null, Admin);
        Save(application, requireTwoFactor);
        application.ClearDomainEvents();

        Save(application, requireTwoFactor);

        application.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Monitor", ApplicationMfaEnforcementMode.Monitor)]
    [InlineData("Enforce", ApplicationMfaEnforcementMode.Enforce)]
    [InlineData(" enforce ", ApplicationMfaEnforcementMode.Enforce)]
    [InlineData("1", ApplicationMfaEnforcementMode.Monitor)]
    [InlineData("Enforced", ApplicationMfaEnforcementMode.Monitor)]
    [InlineData("", ApplicationMfaEnforcementMode.Monitor)]
    [InlineData(null, ApplicationMfaEnforcementMode.Monitor)]
    public void ApplicationMfaEnforcementInForce_ATypoReadsMonitor(string? configured, ApplicationMfaEnforcementMode expected)
    {
        new IdentityProviderSettings { ApplicationMfaEnforcement = configured! }
            .ApplicationMfaEnforcementInForce.Should().Be(expected);
    }

    [Fact]
    public void ApplicationMfaEnforcement_DefaultsToMonitor()
    {
        new IdentityProviderSettings().ApplicationMfaEnforcementInForce.Should().Be(ApplicationMfaEnforcementMode.Monitor);
    }
}
