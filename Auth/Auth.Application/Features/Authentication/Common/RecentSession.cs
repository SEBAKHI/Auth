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
public sealed record RecentSession(
    Guid SessionId,
    string? DeviceName,
    AuthenticationMethods Methods = default);
