using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised after a passkey was added to an account and saved (S20, PK7). Its handlers write
/// the audit row and send the owner the independent notice (FA11).
/// </summary>
/// <remarks>
/// Carries the recipient's address and name so the notice handler does not load the user
/// again, like the two-factor events. It carries no credential id, public key or user handle
/// (PK16): the passkey's row id and the name its owner gave it are what the audit and the
/// notice show.
/// </remarks>
/// <param name="UserId">The account.</param>
/// <param name="PasskeyId">The passkey's row id.</param>
/// <param name="Name">The name the owner gave it.</param>
/// <param name="RegisteredBy">The signed-in account that added it: the owner's own session.</param>
/// <param name="Email">The account's address, for the notice.</param>
/// <param name="DisplayName">The account's display name, for the notice.</param>
/// <param name="DeviceName">The browser and operating system of the session, when known.</param>
/// <param name="OccurredAtUtc">When it was added (UTC).</param>
public sealed record PasskeyRegisteredEvent(
    Guid UserId,
    Guid PasskeyId,
    string Name,
    Guid RegisteredBy,
    string Email,
    string DisplayName,
    string? DeviceName,
    DateTime OccurredAtUtc) : IDomainEvent;
