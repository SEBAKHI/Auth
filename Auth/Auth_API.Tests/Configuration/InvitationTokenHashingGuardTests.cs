namespace Auth_API.Tests.Configuration;

/// <summary>
/// Source-level guards over the invitation-token hashing change.
///
/// <para>
/// The invitation token was the one bearer credential in this system stored in
/// clear text while refresh tokens, authorization codes, password-reset tokens,
/// API keys and every OTP were hashed. A single SELECT on
/// <c>OrganizationInvitations</c> was a working invitation into the organization
/// the row named, with the role it named — a tenant boundary crossed by reading.
/// </para>
///
/// <para>
/// Two things have to stay true together for that to remain fixed, and neither is
/// visible to a behavioural test: the entity must expose no property that invites
/// a caller to store a plaintext token, and the repository must offer no lookup
/// that takes one.
/// </para>
/// </summary>
public class InvitationTokenHashingGuardTests
{
    [Fact]
    public void Entity_ExposesTokenHash_AndNoPlaintextTokenProperty()
    {
        var properties = typeof(Auth.Domain.Entities.OrganizationInvitation)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        properties.Should().Contain("TokenHash");
        properties.Should().NotContain("Token",
            "a property called Token on this entity is an invitation to store one");
    }

    [Fact]
    public void Repository_OffersNoLookupThatTakesAPlaintextToken()
    {
        var methods = typeof(Auth.Domain.Interfaces.Repositories.IOrganizationRepository)
            .GetMethods()
            .Select(m => m.Name)
            .ToList();

        methods.Should().Contain("GetInvitationByTokenHashAsync");
        methods.Should().NotContain("GetInvitationByTokenAsync",
            "leaving the old lookup in place lets a future call site hand it the plaintext " +
            "and silently reintroduce a clear-text comparison");
    }
}
