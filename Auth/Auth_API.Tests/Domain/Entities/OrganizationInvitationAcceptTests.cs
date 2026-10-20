using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// OI-192: accepting an invitation records exactly one application role outcome when the
/// invitation names a role, and none when it does not. The handler computes the outcome, so
/// a mismatch is a programming error and throws rather than reaching the database, where
/// CK_OrganizationInvitations_ApplicationRoleOutcomeNeedsRole would refuse it anyway.
/// </summary>
public class OrganizationInvitationAcceptTests
{
    private static OrganizationInvitation WithApplicationRole() =>
        TestHelpers.CreateOrganizationInvitation(
            applicationRole: InvitationApplicationRole.From(Guid.NewGuid(), Guid.NewGuid()));

    [Theory]
    [InlineData(InvitationApplicationRoleOutcome.Applied)]
    [InlineData(InvitationApplicationRoleOutcome.AlreadyHeld)]
    [InlineData(InvitationApplicationRoleOutcome.Dropped)]
    public void Accept_WithAnApplicationRole_RecordsTheOutcome(InvitationApplicationRoleOutcome outcome)
    {
        var invitation = WithApplicationRole();
        invitation.ApplicationRoleOutcome.Should().BeNull("nothing is recorded while pending");

        var result = invitation.Accept(Guid.NewGuid(), outcome);

        result.IsError.Should().BeFalse();
        invitation.Status.Should().Be(InvitationStatus.Accepted);
        invitation.ApplicationRoleOutcome.Should().Be(outcome);
    }

    [Fact]
    public void Accept_MembershipOnly_RecordsNoOutcome()
    {
        var invitation = TestHelpers.CreateOrganizationInvitation();

        invitation.Accept(Guid.NewGuid()).IsError.Should().BeFalse();

        invitation.ApplicationRoleOutcome.Should().BeNull();
    }

    [Fact]
    public void Accept_WithAnApplicationRoleButNoOutcome_Throws()
    {
        var invitation = WithApplicationRole();

        var accept = () => invitation.Accept(Guid.NewGuid());

        accept.Should().Throw<ArgumentException>();
        invitation.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public void Accept_MembershipOnlyWithAnOutcome_Throws()
    {
        var invitation = TestHelpers.CreateOrganizationInvitation();

        var accept = () => invitation.Accept(Guid.NewGuid(), InvitationApplicationRoleOutcome.Applied);

        accept.Should().Throw<ArgumentException>();
        invitation.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public void Accept_AnAlreadyAcceptedInvitation_KeepsItsRecordedOutcome()
    {
        var invitation = WithApplicationRole();
        invitation.Accept(Guid.NewGuid(), InvitationApplicationRoleOutcome.Applied);

        var again = invitation.Accept(Guid.NewGuid(), InvitationApplicationRoleOutcome.Dropped);

        again.IsError.Should().BeTrue();
        invitation.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Applied);
    }
}
