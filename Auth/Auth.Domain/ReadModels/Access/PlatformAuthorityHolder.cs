using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.ReadModels.Access;

/// <summary>
/// An account that holds platform authority, as one read saw it: who it is,
/// whether it can act and be reached now, and the platform permission codes it
/// holds — the ones its platform token carries.
/// </summary>
public sealed class PlatformAuthorityHolder
{
    public PlatformAuthorityHolder(
        Guid userId,
        string email,
        string displayName,
        UserStatus status,
        bool emailConfirmed,
        bool isDeleted,
        IEnumerable<string> permissionCodes)
    {
        UserId = userId;
        Email = email;
        DisplayName = displayName;
        Status = status;
        EmailConfirmed = emailConfirmed;
        IsDeleted = isDeleted;
        PermissionCodes = permissionCodes.ToArray();
    }

    /// <summary>Gets the account's id.</summary>
    public Guid UserId { get; }

    /// <summary>Gets the account's email address.</summary>
    public string Email { get; }

    /// <summary>Gets the account's display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the account's status when read.</summary>
    public UserStatus Status { get; }

    /// <summary>Gets whether the account's email address was confirmed when read.</summary>
    public bool EmailConfirmed { get; }

    /// <summary>Gets whether the account was waiting for deletion when read.</summary>
    public bool IsDeleted { get; }

    /// <summary>Gets the platform permission codes the account holds, wildcards as stored.</summary>
    public IReadOnlyList<string> PermissionCodes { get; }

    /// <summary>
    /// Whether the account holds <paramref name="permission"/>, by the wildcard rule
    /// every permission check uses (<see cref="PermissionCode.Matches"/>).
    /// </summary>
    public bool Holds(string permission) =>
        PermissionCodes.Any(code => PermissionCode.From(code).Matches(permission));

    /// <summary>
    /// Whether the account could act on another account now, and be told so: it is
    /// not that account, it is active and not waiting for deletion, and its email
    /// address is confirmed.
    /// </summary>
    public bool CanActOn(Guid accountId) =>
        UserId != accountId
        && !IsDeleted
        && Status == UserStatus.Active
        && EmailConfirmed;

    // The address stays out of any log line or assertion message the holder reaches.
    public override string ToString() => $"PlatformAuthorityHolder {{ UserId = {UserId} }}";
}
