using Auth.Domain.Entities;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// The rules a pending self-registration carries: what makes its code live,
/// what a rotation resets and what it keeps, and how the per-address mail
/// window bounds guessing. The repository enforces these under a row lock;
/// the decisions themselves live here so they can be read and tested without
/// a database.
/// </summary>
public class PendingRegistrationTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private const int MaxPerWindow = 3;

    [Fact]
    public void Create_StoresTheAddressInBothShapes_AndCarriesNoCodeYet()
    {
        var pending = PendingRegistration.Create("handle", "  Jane.Doe@Example.COM ", Now);

        pending.Email.Value.Should().Be("jane.doe@example.com", "the account row is written lower-case");
        pending.NormalizedEmail.Should().Be("JANE.DOE@EXAMPLE.COM", "the unique index is on the Users.NormalizedEmail form");
        pending.Handle.Should().Be("handle");
        pending.OtpHash.Should().BeEmpty();
        pending.IsCodeLive(Now).Should().BeFalse("no code has been issued");
        pending.MailedCount.Should().Be(0, "the first code charges the window like every later one");
        pending.WasMailed.Should().BeFalse();
        pending.IsConsumed.Should().BeFalse();
    }

    [Fact]
    public void TheScope_IsBoundToTheRowId_NotTheAddress()
    {
        var pending = PendingRegistration.Create("handle", "jane@one.example", Now);

        pending.OtpScope.Should().Be($"pending-registration:{pending.Id}",
            "a code minted for a user's e-mail verification (scope: the user id) must never verify here, " +
            "and an address is caller-typed input that must not be inside a keyed message");
    }

    [Fact]
    public void AnIssuedCode_IsLive_UntilItExpires_OrTakesFiveWrongGuesses_OrTheRowIsConsumed()
    {
        var pending = Issued();

        pending.IsCodeLive(Now).Should().BeTrue();
        pending.IsCodeLive(Now.AddMinutes(5)).Should().BeFalse("the code expired; expiry is compared, never swept");

        for (var i = 0; i < PendingRegistration.MaxAttempts - 1; i++) pending.RecordFailedAttempt();
        pending.IsCodeLive(Now).Should().BeTrue("four wrong guesses leave one");
        pending.RecordFailedAttempt();
        pending.IsExhausted.Should().BeTrue();
        pending.IsCodeLive(Now).Should().BeFalse("the fifth wrong guess kills the code, not the row");
        pending.IsConsumed.Should().BeFalse("exhaustion never stamps ConsumedAt — that means an account exists");

        var consumed = Issued();
        consumed.Consume(Now);
        consumed.IsCodeLive(Now).Should().BeFalse();
    }

    [Fact]
    public void IssueCode_RotatesInPlace_ResettingTheCodeState_AndKeepingTheAddressCounters()
    {
        var pending = Issued();
        var id = pending.Id;
        pending.RecordFailedAttempt();
        pending.MarkVerified(Now);
        pending.MarkMailed(Now);
        pending.TryChargeMailWindow(Window, MaxPerWindow, Now);

        pending.IssueCode("hash-2", 5, "ar", Now.AddMinutes(1));

        pending.Id.Should().Be(id, "rotation keeps the row; the handle the client holds stays valid");
        pending.Handle.Should().Be("handle", "issuing a code does not change the handle by itself; Rebind does");
        pending.OtpHash.Should().Be("hash-2");
        pending.ExpiresAt.Should().Be(Now.AddMinutes(6));
        pending.AttemptCount.Should().Be(0);
        pending.VerifiedAt.Should().BeNull();
        pending.MailedAt.Should().BeNull("the new code has not reached the outbox yet");
        pending.PreferredLanguage.Should().Be("ar", "the message goes out in the language of the request that issued the code");
        pending.MailedCount.Should().Be(2, "the mail counters belong to the address and survive rotation — they are the guessing bound");
    }

    [Fact]
    public void IssueCode_RefusesAConsumedRow()
    {
        var pending = Issued();
        pending.Consume(Now);

        var act = () => pending.IssueCode("hash-2", 5, null, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TheMailWindow_AllowsAtMostTheCapPerWindow_ThenOpensANewOne()
    {
        var pending = PendingRegistration.Create("handle", "jane@one.example", Now);

        pending.TryChargeMailWindow(Window, MaxPerWindow, Now).Should().BeTrue();
        pending.TryChargeMailWindow(Window, MaxPerWindow, Now.AddSeconds(10)).Should().BeTrue();
        pending.TryChargeMailWindow(Window, MaxPerWindow, Now.AddSeconds(20)).Should().BeTrue();
        pending.MailedCount.Should().Be(MaxPerWindow);

        pending.TryChargeMailWindow(Window, MaxPerWindow, Now.AddSeconds(30)).Should().BeFalse(
            "three codes in one minute is the cap; a fourth must not be minted");
        pending.MailedCount.Should().Be(MaxPerWindow, "a refused charge changes nothing");
        pending.MailWindowStartUtc.Should().Be(Now);

        pending.TryChargeMailWindow(Window, MaxPerWindow, Now.AddSeconds(60)).Should().BeTrue(
            "the window elapsed, so a new one opens");
        pending.MailedCount.Should().Be(1);
        pending.MailWindowStartUtc.Should().Be(Now.AddSeconds(60));
    }

    [Fact]
    public void TheGuessingBound_IsCodesPerWindowTimesAttemptsPerCode()
    {
        // The property the counters exist for: however many clients or
        // addresses an attacker controls, one victim address yields at most
        // cap x MaxAttempts guesses per window. Played out on the entity: mint
        // until the window refuses, guess each code until it dies.
        var pending = PendingRegistration.Create("handle", "jane@one.example", Now);
        var acceptedGuesses = 0;
        var codes = 0;

        while (pending.TryChargeMailWindow(Window, MaxPerWindow, Now.AddSeconds(codes)))
        {
            codes++;
            pending.IssueCode($"hash-{codes}", 5, "en", Now);
            while (pending.IsCodeLive(Now))
            {
                pending.RecordFailedAttempt();
                acceptedGuesses++;
            }
        }

        codes.Should().Be(MaxPerWindow);
        acceptedGuesses.Should().Be(MaxPerWindow * PendingRegistration.MaxAttempts);
    }

    [Fact]
    public void NormalizeKey_IsTheFormTheRowIsStoredUnder()
    {
        // The locked read, the row and the client handle must all derive one
        // string from one address, or a lock is taken on a key the row was
        // never written under.
        var pending = PendingRegistration.Create("handle", "  Jane.Doe@Example.COM ", Now);

        PendingRegistration.NormalizeKey("  Jane.Doe@Example.COM ").Should().Be(pending.NormalizedEmail);
        PendingRegistration.NormalizeKey("jane.doe@example.com").Should().Be(pending.NormalizedEmail);
    }

    [Fact]
    public void Rebind_ReplacesTheHandle()
    {
        var pending = Issued();

        pending.Rebind("handle-under-the-new-key");

        pending.Handle.Should().Be("handle-under-the-new-key");
    }

    [Fact]
    public void TheStamps_KeepTheirFirstValue()
    {
        var pending = Issued();

        pending.MarkMailed(Now);
        pending.MarkMailed(Now.AddMinutes(1));
        pending.MarkVerified(Now);
        pending.MarkVerified(Now.AddMinutes(1));
        pending.Consume(Now);
        pending.Consume(Now.AddMinutes(1));

        pending.MailedAt.Should().Be(Now);
        pending.VerifiedAt.Should().Be(Now);
        pending.ConsumedAt.Should().Be(Now);
    }

    private static PendingRegistration Issued()
    {
        var pending = PendingRegistration.Create("handle", "jane@one.example", Now);
        pending.TryChargeMailWindow(Window, MaxPerWindow, Now);
        pending.IssueCode("hash-1", 5, "en", Now);
        return pending;
    }
}
