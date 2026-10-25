using Auth.Application.Common;
using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for user information.
/// </summary>
public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>Absolute URL of the user's profile image (composed from the stored key); null when unset.</summary>
    public string? ProfileImageUrl { get; set; }

    public UserStatus Status { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneConfirmed { get; set; }
    /// <summary>
    /// Whether a sign-in asks for a second step: the account flag. True for either
    /// second factor; <see cref="TotpEnabled"/> and <see cref="EmailCodeEnabled"/> say which.
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    // The four members below read the factor row, which only the single-user read
    // loads (GET users/{id} and users/me). Every other response leaves them null, and
    // null is omitted from the JSON: absent means "not said here", never "no".

    /// <summary>Whether the account's second factor is an authenticator app (S24); null where not read.</summary>
    public bool? TotpEnabled { get; set; }

    /// <summary>Whether the account's second factor is the emailed code (S24); null where not read.</summary>
    public bool? EmailCodeEnabled { get; set; }

    /// <summary>
    /// Whether the account has a second factor a two-factor policy accepts (FA2): the
    /// authenticator app. The emailed code never is one. Null where not read.
    /// </summary>
    public bool? QualifyingMfa { get; set; }

    /// <summary>
    /// Whether the emailed code can be offered right now: email sending is on. Read per
    /// request; a screen offers the emailed code only while it is true. Null where not read.
    /// </summary>
    public bool? EmailCodeAvailable { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? TimeZone { get; set; }
    public string? Theme { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Security / account fields
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public DateTime? PasswordExpiresUtc { get; set; }
    public bool MustChangePassword { get; set; }

    /// <summary>Whether a local password is set. External-only accounts have none, which switches sensitive re-authentication to the emailed-code path.</summary>
    public bool HasPassword { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    /// <summary>Display name of the creating user; null when unresolved.</summary>
    public string? CreatedByName { get; set; }

    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }

    /// <summary>Display name of the last modifying user; null when unresolved.</summary>
    public string? ModifiedByName { get; set; }

    /// <summary>Whether the account is soft-deleted. Only surfaced to callers who requested deleted accounts.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp of the soft deletion; null while the account is live.</summary>
    public DateTime? DeletedAt { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];

    /// <summary>
    /// Which actions the caller may take on this account, by the rules the actions
    /// themselves enforce. Absent when not computed for a caller (every response but
    /// the user reads), which a client treats as allowed.
    /// </summary>
    public UserAllowedActionsDto? AllowedActions { get; set; }
}

/// <summary>
/// The caller's allowed actions on one account. Each is whether the action would
/// pass the account rules now: never the system account, only by an administrator
/// who covers the account's platform permissions, never leaving the platform without
/// a full administrator. Whether the action fits the account's state (locking a
/// locked account) is not part of it.
/// </summary>
public class UserAllowedActionsDto
{
    /// <summary>Editing the profile (<c>PUT users/{id}</c>).</summary>
    public bool Update { get; set; }

    /// <summary>Setting or removing the profile image (<c>PUT/DELETE users/{id}/profile-image</c>).</summary>
    public bool ProfileImage { get; set; }

    /// <summary><c>POST users/{id}/lock</c>.</summary>
    public bool Lock { get; set; }

    /// <summary><c>POST users/{id}/unlock</c>.</summary>
    public bool Unlock { get; set; }

    /// <summary><c>POST users/{id}/activate</c>.</summary>
    public bool Activate { get; set; }

    /// <summary><c>POST users/{id}/deactivate</c>.</summary>
    public bool Deactivate { get; set; }

    /// <summary><c>DELETE users/{id}</c>.</summary>
    public bool Delete { get; set; }

    /// <summary><c>DELETE users/{id}/permanent</c>.</summary>
    public bool PermanentDelete { get; set; }

    /// <summary>
    /// <c>POST users/{id}/two-factor/reset</c>: never one's own account or the system
    /// account, and only by an administrator who covers the account's platform
    /// permissions, the reset's own rules. Whether the account has a factor, or the
    /// caller's own session is recent enough, is not part of it.
    /// </summary>
    public bool ResetTwoFactor { get; set; }

    /// <summary>
    /// Asking for this account's own deletion (<c>POST users/me/deletion</c>); present
    /// only when the caller reads its own account. False means the account is the
    /// last full administrator.
    /// </summary>
    public bool? RequestDeletion { get; set; }

    /// <summary>The caller's allowed actions on <paramref name="accountId"/>, as <paramref name="decisions"/> answers them.</summary>
    public static UserAllowedActionsDto For(AccountActionDecisions decisions, Guid accountId) => new()
    {
        Update = decisions.Allows(accountId, AccountAction.Update),
        ProfileImage = decisions.Allows(accountId, AccountAction.ProfileImage),
        Lock = decisions.Allows(accountId, AccountAction.Lock),
        Unlock = decisions.Allows(accountId, AccountAction.Unlock),
        Activate = decisions.Allows(accountId, AccountAction.Activate),
        Deactivate = decisions.Allows(accountId, AccountAction.Deactivate),
        Delete = decisions.Allows(accountId, AccountAction.Delete),
        PermanentDelete = decisions.Allows(accountId, AccountAction.PermanentDelete),
        ResetTwoFactor = decisions.Allows(accountId, AccountAction.ResetTwoFactor),
        RequestDeletion = decisions.ActorId == accountId
            ? decisions.Allows(accountId, AccountAction.RequestOwnDeletion)
            : null
    };
}

/// <summary>
/// Paginated result for users.
/// </summary>
public class PagedUsersDto
{
    public IReadOnlyList<UserDto> Users { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
