using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Signs step-up tickets with the server's HMAC key, reusing the same keyed hash
/// that protects refresh tokens at rest — one key, one place it is loaded from.
/// </summary>
/// <remarks>
/// Wire format is <c>{unixSeconds}|{clientId}|{signature}</c>. The signature
/// covers <c>{unixSeconds}|{clientId}|{codeChallenge}</c>, so a browser that edits
/// either visible field invalidates it, and so does presenting the ticket with any
/// other authorize request (OI-150). The challenge is signed but never carried:
/// the validator recomputes the signature from the request in hand, which keeps
/// the format and puts nothing of the request into the cookie. A ticket written
/// before the challenge was signed fails validation and counts as no ticket: one
/// extra sign-in for a person caught mid-flow by the deploy, and no format
/// version to read. Nothing secret is inside: the ticket is a timestamp the
/// server vouches for, not a credential.
/// </remarks>
public class StepUpTicketService : IStepUpTicketService
{
    private const char Separator = '|';

    private readonly IRefreshTokenKeyService _keyService;

    public StepUpTicketService(IRefreshTokenKeyService keyService)
    {
        _keyService = keyService;
    }

    /// <inheritdoc />
    public string Issue(string clientId, string codeChallenge, DateTime demandedAtUtc)
    {
        // The authorize handler validated the challenge before raising a demand;
        // an empty one here would sign a ticket no request can ever present.
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

        var demandedAt = new DateTimeOffset(demandedAtUtc, TimeSpan.Zero);

        // Rounded UP to the next whole second. The wire format carries seconds, so
        // truncating would place the recorded demand slightly BEFORE it happened —
        // and a session minted in that sub-second gap would then look like the
        // answer to a demand it actually predates. Rounding up removes the gap
        // rather than arguing about how small it is.
        var unixSeconds = demandedAt.ToUnixTimeMilliseconds() % 1000 == 0
            ? demandedAt.ToUnixTimeSeconds()
            : demandedAt.ToUnixTimeSeconds() + 1;

        var visible = BuildVisiblePart(unixSeconds, clientId);
        var signature = _keyService.ComputeTokenHash(BuildSignedPayload(visible, codeChallenge));

        // The reader finds the signature by splitting at the LAST separator, which
        // is only unambiguous while the signature itself contains none. That holds
        // for base64 (the current format) but is not guaranteed by the interface,
        // and if it ever stopped holding every ticket would silently fail to
        // validate and prompt=login would loop. Fail here instead, loudly.
        if (signature.Contains(Separator))
        {
            throw new InvalidOperationException(
                $"Step-up ticket signatures must not contain '{Separator}'.");
        }

        return $"{visible}{Separator}{signature}";
    }

    /// <inheritdoc />
    public bool TryValidate(
        string? ticket,
        string clientId,
        string? codeChallenge,
        DateTime nowUtc,
        TimeSpan lifetime,
        out DateTime demandedAtUtc)
    {
        demandedAtUtc = default;

        if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(codeChallenge))
        {
            return false;
        }

        // Split at the FIRST and LAST separator rather than on every one: a
        // client id is free to contain the separator character, and splitting
        // naively would reject those outright.
        var firstBreak = ticket.IndexOf(Separator);
        var lastBreak = ticket.LastIndexOf(Separator);
        if (firstBreak <= 0 || lastBreak <= firstBreak || lastBreak == ticket.Length - 1)
        {
            return false;
        }

        var visible = ticket[..lastBreak];
        var signature = ticket[(lastBreak + 1)..];

        // The challenge comes from the request in hand, never from the ticket: a
        // ticket raised by another request carries a signature over another one.
        if (!SignatureMatches(BuildSignedPayload(visible, codeChallenge), signature))
        {
            return false;
        }

        // Only now is the content trustworthy enough to read.
        if (!long.TryParse(
                ticket[..firstBreak],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var unixSeconds))
        {
            return false;
        }

        var ticketClientId = ticket[(firstBreak + 1)..lastBreak];
        if (!string.Equals(ticketClientId, clientId, StringComparison.Ordinal))
        {
            // A demand raised for one application must not be answerable by
            // another, or a client the user is merely entitled to could mint
            // tickets that satisfy a sensitive client's re-authentication.
            return false;
        }

        var demandedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        // A ticket from the future is a clock problem or a forgery attempt with a
        // leaked key; either way it must not be honoured, because a future
        // timestamp can never be satisfied by a real session and would loop.
        if (demandedAt > nowUtc || nowUtc - demandedAt > lifetime)
        {
            return false;
        }

        demandedAtUtc = demandedAt;
        return true;
    }

    private bool SignatureMatches(string payload, string signature)
    {
        var expected = _keyService.ComputeTokenHash(payload);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    private static string BuildVisiblePart(long unixSeconds, string clientId)
        => $"{unixSeconds.ToString(CultureInfo.InvariantCulture)}{Separator}{clientId}";

    /// <remarks>
    /// Unambiguous although a client id may contain the separator: the reader
    /// requires the visible client id to equal the request's exactly, so the rest
    /// of the signed text can only be that request's challenge.
    /// </remarks>
    private static string BuildSignedPayload(string visiblePart, string codeChallenge)
        => $"{visiblePart}{Separator}{codeChallenge}";
}
