using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Signs the application two-step tickets with the credential hasher, under the keyring's
/// own purpose for them (KR9, S23): never the step-up ticket's key.
/// </summary>
/// <remarks>
/// Wire format is <c>{unixSeconds}|{clientId}|{userId}|{reason}|{cc}|{signature}</c>, where
/// <c>{userId}</c> is the user's id with no separators, <c>{reason}</c> the
/// <see cref="ApplicationMfaRequirement"/> number, and <c>{cc}</c> the base64url SHA-256 of
/// the request's <c>code_challenge</c>. The signature covers everything before it. A client
/// id may contain the separator, so the fields are read from both ends: the time before
/// the first separator, the four fixed fields after the client id from the end.
/// </remarks>
public sealed class AppMfaTicketService : IAppMfaTicketService
{
    /// <summary>
    /// How long a ticket answers: fixed, long enough to set up an authenticator on the way
    /// (AM8 (8-1)). The cookie's lifetime and the keyring's overlap for the purpose match it.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private const char Separator = '|';

    // Reason, user and challenge fingerprint, plus the signature: the fields after the client id.
    private const int TrailingFields = 4;

    private readonly ICredentialHasher _credentialHasher;

    public AppMfaTicketService(ICredentialHasher credentialHasher)
    {
        _credentialHasher = credentialHasher;
    }

    /// <inheritdoc />
    public string Issue(
        string clientId, Guid userId, ApplicationMfaRequirement reason, string codeChallenge, DateTime issuedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);
        if (reason == ApplicationMfaRequirement.None || !Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A ticket records a reason to sign in again.");
        }

        // Truncated to the second, unlike the step-up ticket, which rounds up so that no
        // session minted in the gap answers it. Here the time only bounds the lifetime, and
        // rounding up would make a return within the same second read as "from the future":
        // the guard would miss the fastest loops.
        var unixSeconds = new DateTimeOffset(issuedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds();

        var signed = string.Join(
            Separator,
            unixSeconds.ToString(CultureInfo.InvariantCulture),
            clientId,
            userId.ToString("N"),
            ((int)reason).ToString(CultureInfo.InvariantCulture),
            ChallengeFingerprint(codeChallenge));
        var signature = _credentialHasher.Hash(CredentialPurpose.AppMfaTicket, signed);

        // The reader takes the signature after the LAST separator; fail here, loudly, rather
        // than issue tickets that never read.
        if (signature.Contains(Separator))
        {
            throw new InvalidOperationException($"Ticket signatures must not contain '{Separator}'.");
        }

        return $"{signed}{Separator}{signature}";
    }

    /// <inheritdoc />
    public bool TryRead(
        string? ticket,
        string clientId,
        Guid userId,
        string? codeChallenge,
        DateTime nowUtc,
        out ApplicationMfaRequirement reason)
    {
        reason = ApplicationMfaRequirement.None;

        if (!TryReadVerified(ticket, clientId, codeChallenge, nowUtc, out var fields) ||
            !string.Equals(fields.UserId, userId.ToString("N"), StringComparison.Ordinal))
        {
            return false;
        }

        reason = fields.Reason;
        return true;
    }

    /// <inheritdoc />
    public bool BelongsTo(string? ticket, string clientId, string? codeChallenge, DateTime nowUtc) =>
        TryReadVerified(ticket, clientId, codeChallenge, nowUtc, out _);

    /// <summary>
    /// The ticket's fields, once its signature verifies and its client, request and age
    /// match. The user is left to the caller.
    /// </summary>
    private bool TryReadVerified(
        string? ticket, string clientId, string? codeChallenge, DateTime nowUtc, out TicketFields fields)
    {
        fields = default;

        if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(codeChallenge))
        {
            return false;
        }

        var firstBreak = ticket.IndexOf(Separator);
        var breaks = new int[TrailingFields];
        var position = ticket.Length;
        for (var i = 0; i < TrailingFields; i++)
        {
            position = position > 0 ? ticket.LastIndexOf(Separator, position - 1) : -1;
            if (position <= firstBreak)
            {
                return false;
            }

            breaks[i] = position;
        }

        // breaks[0] is the last separator (before the signature); breaks[3] ends the client id.
        var signed = ticket[..breaks[0]];
        var signature = ticket[(breaks[0] + 1)..];
        if (signature.Length == 0 ||
            !_credentialHasher.Verify(CredentialPurpose.AppMfaTicket, signed, signature))
        {
            return false;
        }

        // Only now is the content trustworthy enough to read.
        var ticketClientId = ticket[(firstBreak + 1)..breaks[3]];
        var ticketUserId = ticket[(breaks[3] + 1)..breaks[2]];
        var ticketReason = ticket[(breaks[2] + 1)..breaks[1]];
        var ticketChallenge = ticket[(breaks[1] + 1)..breaks[0]];

        if (!string.Equals(ticketClientId, clientId, StringComparison.Ordinal) ||
            !string.Equals(ticketChallenge, ChallengeFingerprint(codeChallenge), StringComparison.Ordinal) ||
            !long.TryParse(ticket[..firstBreak], NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds) ||
            !int.TryParse(ticketReason, NumberStyles.None, CultureInfo.InvariantCulture, out var reasonNumber) ||
            !Enum.IsDefined(typeof(ApplicationMfaRequirement), reasonNumber) ||
            reasonNumber == (int)ApplicationMfaRequirement.None)
        {
            return false;
        }

        // A ticket from the future is a clock problem or a forgery with a leaked key.
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        if (issuedAt > nowUtc || nowUtc - issuedAt > Lifetime)
        {
            return false;
        }

        fields = new TicketFields(ticketUserId, (ApplicationMfaRequirement)reasonNumber);
        return true;
    }

    /// <summary>
    /// The request's challenge as the ticket carries it: base64url SHA-256, never the
    /// challenge itself.
    /// </summary>
    private static string ChallengeFingerprint(string codeChallenge) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(codeChallenge)));

    private readonly record struct TicketFields(string UserId, ApplicationMfaRequirement Reason);
}
