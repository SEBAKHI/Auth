using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Domain;

/// <summary>
/// OI-215 T6, T9, T10: an account may have no name and no password. Its name to show is
/// its address, never a space; its password goes only by its owner's removal or by the
/// first confirmation of its address, each announced.
/// </summary>
public class UserPasswordlessAndNamelessTests
{
    // ── T9: names ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Jane", "Doe", "Jane Doe")]
    [InlineData("Jane", "", "Jane")]
    [InlineData("", "Doe", "Doe")]
    [InlineData("", "", "")]
    [InlineData(" ", " ", "")]
    public void GetFullName_LeavesOutABlankPart_AndIsNeverALoneSpace(string first, string last, string expected)
    {
        TestHelpers.CreateUser(firstName: first, lastName: last).GetFullName().Should().Be(expected);
    }

    [Theory]
    [InlineData("Jane", "Doe", "Jane Doe")]
    [InlineData("", "", "jane@one.example")]
    [InlineData(" ", "", "jane@one.example")]
    public void NameToShow_IsTheNameWhenThereIsOne_OtherwiseTheAddress(string first, string last, string expected)
    {
        TestHelpers.CreateUser(email: "jane@one.example", firstName: first, lastName: last).NameToShow.Should().Be(expected);
    }

    [Fact]
    public void TheStoredDisplayName_OfANamelessAccount_IsReadAsNone()
    {
        // The store computes FullName with a space between the parts whatever they hold.
        var user = new User(
            Guid.NewGuid(), "jane@one.example", "JANE@ONE.EXAMPLE", null, "", "", " ", null,
            Auth.Domain.Enums.UserStatus.Active, true, false, false, 0, null, null, null, false,
            "en", "UTC", null, false, DateTime.UtcNow, Guid.Empty, null, null);

        user.DisplayName.Should().BeNull("a space is not a name");
        User.NameToShowFrom(" ", "jane@one.example").Should().Be("jane@one.example");
        User.NameToShowFrom("Jane Doe ", "jane@one.example").Should().Be("Jane Doe");
    }

    [Fact]
    public void Create_WithoutAPasswordOrAName_IsAPasswordlessNamelessAccount()
    {
        var user = User.Create("Jane@One.Example", passwordHash: null, firstName: "", lastName: "", createdBy: Guid.Empty);

        user.PasswordHash.Should().BeNull();
        user.PasswordChangedAt.Should().BeNull("no password was ever set");
        user.DisplayName.Should().BeNull();
        user.NameToShow.Should().Be("jane@one.example");
    }

    // ── T10: the owner's removal ─────────────────────────────────────────────

    [Fact]
    public void RemovePassword_RemovesIt_ClearsTheForcedChange_AndAnnouncesIt()
    {
        var user = TestHelpers.CreateUser(email: "jane@one.example");
        user.RequirePasswordChange(Guid.NewGuid());
        user.ClearDomainEvents();

        var result = user.RemovePassword(user.Id);

        result.IsError.Should().BeFalse();
        user.PasswordHash.Should().BeNull();
        user.MustChangePassword.Should().BeFalse();
        user.DomainEvents.OfType<PasswordRemovedEvent>().Should().ContainSingle()
            .Which.Should().Be(new PasswordRemovedEvent(user.Id, user.Id, "jane@one.example", user.NameToShow, PasswordRemovalReasons.Owner));
    }

    [Fact]
    public void RemovePassword_WithoutAPassword_IsRefused()
    {
        var user = TestHelpers.CreateUser(passwordHash: null);

        user.RemovePassword(user.Id).FirstError.Should().Be(UserErrors.PasswordNotSet);
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RemovePassword_OnAnUnconfirmedAddress_IsRefused()
    {
        var user = TestHelpers.CreateUser(emailConfirmed: false);

        user.RemovePassword(user.Id).FirstError.Code.Should().Be(UserErrors.EmailNotConfirmed.Code);
        user.PasswordHash.Should().NotBeNull("the code that would let it back in goes to an address nobody proved");
    }

    // ── T6: the first confirmation by code ───────────────────────────────────

    [Fact]
    public void ConfirmEmailBySignInCode_RetiresAPasswordSetBeforeTheProof()
    {
        var user = TestHelpers.CreateUser(emailConfirmed: false);
        var stamp = user.PasswordChangedAt;

        var removed = user.ConfirmEmailBySignInCode(user.Id);

        removed.Should().BeTrue();
        user.EmailConfirmed.Should().BeTrue();
        user.PasswordHash.Should().BeNull();
        user.PasswordChangedAt.Should().Be(stamp, "the mailbox was proven, never a password");
        user.DomainEvents.OfType<PasswordRemovedEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be(PasswordRemovalReasons.SetBeforeAddressProven);
    }

    [Fact]
    public void ConfirmEmailBySignInCode_OnAConfirmedAddress_ChangesNothing()
    {
        var user = TestHelpers.CreateUser(emailConfirmed: true);

        user.ConfirmEmailBySignInCode(user.Id).Should().BeFalse();
        user.PasswordHash.Should().Be("TestPasswordHash");
        user.DomainEvents.Should().BeEmpty();
    }
}
