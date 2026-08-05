using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// The reservation window is derived, not chosen.
///
/// <para>
/// A destroyed e-mail address may only be released once every record still
/// keyed to that address has expired. Release it earlier and the next holder
/// inherits the previous one's history — the sharpest case being a pending
/// organization invitation, which binds on the address string rather than on a
/// user id, so inheriting one is a membership grant.
/// </para>
///
/// <para>
/// The audit log is the longest-lived of those records, so it sets the floor.
/// These tests pin that relationship: it is the kind of coupling that is
/// obvious while writing it and invisible six months later when someone lowers
/// a number in the console.
/// </para>
/// </summary>
public class IdentifierReservationWindowTests
{
    [Fact]
    public void EffectiveWindow_UsesTheConfiguredValue_WhenItAlreadyClearsTheFloor()
    {
        var settings = new AccountDeletionSettings
        {
            IdentifierReservationDays = 2000,
            AuditLogRetentionDays = 1095
        };

        settings.EffectiveIdentifierReservationDays.Should().Be(2000);
    }

    [Fact]
    public void EffectiveWindow_IsRaisedToTheAuditRetention_WhenTheConfiguredValueIsShorter()
    {
        var settings = new AccountDeletionSettings
        {
            IdentifierReservationDays = 1095,
            AuditLogRetentionDays = 3650
        };

        settings.EffectiveIdentifierReservationDays.Should().Be(3650,
            "an address released while audit rows keyed to it survive lets the next holder of that " +
            "address inherit the previous holder's history");
    }

    [Fact]
    public void Defaults_AreConsistentWithEachOther()
    {
        var settings = new AccountDeletionSettings();

        settings.EffectiveIdentifierReservationDays.Should().Be(settings.IdentifierReservationDays,
            "the shipped defaults must not silently disagree with the value the console displays");
    }

    [Fact]
    public void PublishedPolicy_QuotesTheEffectiveWindow_NotTheRawSetting()
    {
        // The policy tells users how long their address stays blocked. Quoting
        // the raw setting would understate the window actually enforced.
        var settings = new AccountDeletionSettings
        {
            IdentifierReservationDays = 1095,
            AuditLogRetentionDays = 3650
        };

        GetPublishedPrivacyPolicyQueryHandler.BuildDisclosure(settings, new DataControllerSettings())
            .IdentifierReservationDays.Should().Be(3650);
    }
}
