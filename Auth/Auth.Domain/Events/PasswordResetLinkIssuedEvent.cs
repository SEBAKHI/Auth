using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a signed-in person had a password reset link sent to an account's address (S13):
/// an administrator for another account, or the account's holder for their own, to set a
/// password. The anonymous "forgot password" request raises nothing: anyone may send it.
/// </summary>
/// <param name="EventId">Generated once, when the event is made.</param>
/// <param name="UserId">The account whose address received the link.</param>
/// <param name="TriggeredBy">Who asked for it: the administrator, or the holder themselves.</param>
/// <param name="OccurredAt">When the link was issued (UTC).</param>
/// <remarks>
/// A live link already in the mailbox is not replaced (one live link per account), and then
/// nothing is raised: no link was issued. Carries no token and no address.
/// </remarks>
public sealed record PasswordResetLinkIssuedEvent(
    Guid EventId,
    Guid UserId,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
