using Auth.Infrastructure.Authentication;
using Google.Apis.Auth;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-243 T4: a provider is the mailbox's authority only for an address it verified
/// AND hosts or manages. An outside address the provider checked once is not: the
/// person may have lost that mailbox since, and only an emailed code reaches it.
/// </summary>
public class ProviderMailboxTests
{
    [Theory]
    [InlineData("person@gmail.com")]
    [InlineData("Person@GoogleMail.com ")]
    public void Google_ControlsAVerifiedGoogleAddress(string email)
    {
        GoogleAuthProvider.ControlsMailbox(emailVerified: true, email).Should().BeTrue();
    }

    [Theory]
    [InlineData("person@example.com", true, "an outside address on a Google account")]
    [InlineData("person@mail.gmail.com", true, "a subdomain is another domain")]
    [InlineData("person@gmail.com.example.com", true, "a domain that only starts like Google's")]
    [InlineData("person@gmail.com.", true, "a trailing dot is another domain")]
    [InlineData("person@gmail.com", false, "an unverified address")]
    [InlineData("gmail.com", true, "no address at all")]
    [InlineData("person@icloud.com", true, "another provider's address")]
    public void Google_ControlsNothingElse(string email, bool verified, string because)
    {
        GoogleAuthProvider.ControlsMailbox(verified, email).Should().BeFalse(because);
    }

    /// <summary>
    /// Review of #108 (F1): a token whose hd claim names the address's own domain is a
    /// Google-run identity (Workspace, Cloud Identity), not a Google-run mailbox: the
    /// mail may live anywhere. Read through the provider's real mapping of a payload.
    /// </summary>
    [Theory]
    [InlineData("person@example.com", "example.com")]
    [InlineData("person@example.com", "EXAMPLE.COM")]
    public void Google_ReadsNoMailboxFromTheHdClaim(string email, string hostedDomain)
    {
        var info = GoogleAuthProvider.ToUserInfo(new GoogleJsonWebSignature.Payload
        {
            Subject = "google-sub",
            Email = email,
            EmailVerified = true,
            HostedDomain = hostedDomain,
        });

        info.EmailVerified.Should().BeTrue();
        info.ControlsMailbox.Should().BeFalse();
    }

    [Fact]
    public void Google_ReadsTheMailboxOfAGoogleAddress_ThroughTheSameMapping()
    {
        GoogleAuthProvider.ToUserInfo(new GoogleJsonWebSignature.Payload
        {
            Subject = "google-sub",
            Email = "person@gmail.com",
            EmailVerified = true,
        }).ControlsMailbox.Should().BeTrue();
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
    [InlineData("person@gmail.com", true, "another provider's address")]
    public void Apple_ControlsNothingElse(string email, bool verified, string because)
    {
        AppleAuthProvider.ControlsMailbox(verified, email).Should().BeFalse(because);
    }
}
