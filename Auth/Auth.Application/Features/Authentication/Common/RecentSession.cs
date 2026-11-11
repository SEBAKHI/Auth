using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// A session whose sign-in is recent enough to change the second factor.
/// </summary>
/// <param name="SessionId">The session's ID — the access token's <c>sid</c>.</param>
/// <param name="DeviceName">
/// The browser and operating system the session signed in from, when known, for
/// the notice that tells the owner where the change was made.
/// </param>
/// <param name="Methods">
/// What the session proved when it signed in, and since: the rule that binding a
/// further factor needs a session that proved two.
/// </param>
/// <param name="ProvedAtUtc">
/// When what <paramref name="Methods"/> carries was proved (OI-280 PR B, D-B2): the session's
/// sign-in while that is recent, or past it the last proof inside the session. A proof given in
/// place after a recent sign-in is dated at the sign-in, earlier than it was, so a rule that asks
/// how recent it is fails safe. Absent (the default) is long ago: such a session is never recent
/// enough for a rule that reads this.
/// </param>
public sealed record RecentSession(
    Guid SessionId,
    string? DeviceName,
    AuthenticationMethods Methods = default,
    DateTime ProvedAtUtc = default);
