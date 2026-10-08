using Auth.Domain.Constants;
using Auth.Domain.Enums;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S08: the three claim names and the requirement vocabulary are a contract with
/// the clients (the consoles key on them) and with relying parties (amr and
/// auth_time are OIDC Core §2 names). A rename is a break, so the values are
/// pinned literally.
/// </summary>
public class JwtClaimNamesGuardTests
{
    [Fact]
    public void TheAuthenticationClaimNames_AreLiteral()
    {
        JwtClaimNames.Amr.Should().Be("amr");
        JwtClaimNames.AuthTime.Should().Be("auth_time");
        JwtClaimNames.MfaRequirement.Should().Be("mfa_req");
    }

    [Fact]
    public void TheRequirementVocabulary_IsLiteral_AndCoversEveryRequirement()
    {
        MfaRequirement.None.ToClaimValue().Should().Be("none");
        MfaRequirement.Enroll.ToClaimValue().Should().Be("enroll");
        MfaRequirement.StepUp.ToClaimValue().Should().Be("step_up");
        MfaRequirement.Reauthenticate.ToClaimValue().Should().Be("reauthenticate");

        Enum.GetValues<MfaRequirement>().Select(r => r.ToClaimValue())
            .Should().OnlyHaveUniqueItems().And.HaveCount(4);
    }

    [Fact]
    public void TheRequirementNumbers_NeverMove()
    {
        ((int)MfaRequirement.None).Should().Be(0);
        ((int)MfaRequirement.Enroll).Should().Be(1);
        ((int)MfaRequirement.StepUp).Should().Be(2);
        ((int)MfaRequirement.Reauthenticate).Should().Be(3);
        ((int)LoginCommitOutcome.SessionLost).Should().Be(8);
    }
}
