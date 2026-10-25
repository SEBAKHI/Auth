using Auth.Infrastructure.Authentication;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-243 T4: a provider is the mailbox's authority only for an address it verified
/// AND hosts or manages. An outside address the provider checked once is not: the
/// person may have lost that mailbox since, and only an emailed code reaches it.
/// </summary>
public class ProviderMailboxTests
{
    [Theory]
    [InlineData("person@gmail.com", null)]
    [InlineData("Person@GoogleMail.com ", null)]
    [InlineData("person@example.com", "example.com")]
    [InlineData("person@Example.com", "EXAMPLE.COM")]
    public void Google_ControlsAVerifiedGoogleAddress_OrOneOfTheWorkspaceDomainTheTokenNames(string email, string? hostedDomain)
    {
        GoogleAuthProvider.ControlsMailbox(emailVerified: true, email, hostedDomain).Should().BeTrue();
    }

    [Theory]
    [InlineData("person@example.com", null, true, "an outside address on a Google account")]
    [InlineData("person@example.com", "example.org", true, "a Workspace domain other than the address's")]
    [InlineData("person@sub.example.com", "example.com", true, "a subdomain is another domain")]
    [InlineData("person@example.com", "  ", true, "an empty hd claim")]
    [InlineData("person@gmail.com", null, false, "an unverified address")]
    [InlineData("person@example.com", "example.com", false, "an unverified Workspace address")]
    [InlineData("gmail.com", null, true, "no address at all")]
    public void Google_ControlsNothingElse(string email, string? hostedDomain, bool verified, string because)
    {
        GoogleAuthProvider.ControlsMailbox(verified, email, hostedDomain).Should().BeFalse(because);
    }

    [Theory]
    [InlineData("person@icloud.com")]
    [InlineData("person@me.com")]
    [InlineData("person@MAC.com")]
    [InlineData("abc123@privaterelay.appleid.com")]
    public void Apple_ControlsAVerifiedAppleAddress_OrItsRelay(string email)
    {
        AppleAuthProvider.ControlsMailbox(emailVerified: true, email).Should().BeTrue();
    }

    [Theory]
    [InlineData("person@example.com", true, "an outside address on an Apple ID")]
    [InlineData("person@icloud.com.example.com", true, "a domain that only starts like Apple's")]
    [InlineData("person@icloud.com", false, "an unverified address")]
    public void Apple_ControlsNothingElse(string email, bool verified, string because)
    {
        AppleAuthProvider.ControlsMailbox(verified, email).Should().BeFalse(because);
    }
}
