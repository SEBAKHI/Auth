using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents an invitation to join an organization.
/// Invitations are sent via email and can be accepted, declined, or expire.
/// </summary>
public class OrganizationInvitation : EntityBase
{
    /// <summary>
    /// Gets the ID of the organization.
    /// </summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Gets the email address the invitation was sent to.
    /// </summary>
    public Email Email { get; private set; } = Email.From(string.Empty);

    /// <summary>
    /// Gets the ID of the organization-level role to assign upon acceptance.
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// Gets the HMAC-SHA256 hash of the token used to accept or decline the
    /// invitation. The plaintext token exists only in the invitation email and in
    /// the caller's request; it is never stored and cannot be recovered from here.
    /// </summary>
    /// <remarks>
    /// This was the one bearer credential in the system kept in plaintext while
    /// every other one — refresh tokens, authorization codes, password-reset
    /// tokens, API keys, OTPs — was hashed. Anyone who could read one row could
    /// join the organization it named, with the role it named.
    /// <para>
    /// The database column is still called <c>Token</c>, deliberately: the value is
    /// a 44-character base64 hash, the column is already
    /// <c>NVARCHAR(500) NOT NULL UNIQUE</c>, and a hash is exactly as unique as the
    /// token it stands for — so no schema change, no DACPAC column rename, and no
    /// data-loss risk on publish. The property carries the honest name instead.
    /// </para>
    /// </remarks>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the status of the invitation.
    /// </summary>
    public InvitationStatus Status { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the invitation expires.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who sent the invitation.
    /// </summary>
    public Guid InvitedBy { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the invitation was accepted.
    /// </summary>
    public DateTime? AcceptedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who accepted the invitation.
    /// This may differ from the email if the user already has an account with a different email.
    /// </summary>
    public Guid? AcceptedByUserId { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the invitation was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    private OrganizationInvitation() : base()
    {
    }

    public OrganizationInvitation(
        Guid id,
        Guid organizationId,
        string email,
        Guid roleId,
        string tokenHash,
        InvitationStatus status,
        DateTime expiresAt,
        Guid invitedBy,
        DateTime? acceptedAt,
        Guid? acceptedByUserId,
        DateTime createdAt) : base(id)
    {
        OrganizationId = organizationId;
        Email = Email.From(email);
        RoleId = roleId;
        TokenHash = tokenHash;
        Status = status;
        ExpiresAt = expiresAt;
        InvitedBy = invitedBy;
        AcceptedAt = acceptedAt;
        AcceptedByUserId = acceptedByUserId;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new organization invitation.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="email">The email to invite</param>
    /// <param name="roleId">The org-level role to assign upon acceptance</param>
    /// <param name="tokenHash">
    /// The HMAC hash of the invitation token. Callers must hash before calling:
    /// the plaintext token belongs in the email and nowhere else.
    /// </param>
    /// <param name="invitedBy">Who sent the invitation</param>
    /// <param name="expiresInDays">Number of days until expiration (default 7)</param>
    /// <returns>New OrganizationInvitation instance</returns>
    public static OrganizationInvitation Create(
        Guid organizationId,
        string email,
        Guid roleId,
        string tokenHash,
        Guid invitedBy,
        int expiresInDays = 7)
    {
        return new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email = Email.From(email.ToLowerInvariant().Trim()),
            RoleId = roleId,
            TokenHash = tokenHash,
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays),
            InvitedBy = invitedBy,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Checks if the invitation can be accepted (pending and not expired).
    /// </summary>
    public bool CanBeAccepted()
    {
        return Status == InvitationStatus.Pending && !IsExpired();
    }

    /// <summary>
    /// Checks if the invitation has expired.
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Accepts the invitation.
    /// </summary>
    /// <param name="acceptedByUserId">The user ID who accepted</param>
    public ErrorOr<Success> Accept(Guid acceptedByUserId)
    {
        if (!CanBeAccepted())
        {
            return Status switch
            {
                InvitationStatus.Accepted => OrganizationErrors.InvitationAlreadyAccepted,
                InvitationStatus.Declined => OrganizationErrors.InvitationAlreadyDeclined,
                InvitationStatus.Cancelled => OrganizationErrors.InvitationAlreadyCancelled,
                _ when IsExpired() => OrganizationErrors.InvitationExpired,
                _ => Error.Validation(code: "Organization.InvitationCannotBeAccepted",
                    description: "Invitation cannot be accepted.")
            };
        }

        Status = InvitationStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        AcceptedByUserId = acceptedByUserId;
        return Result.Success;
    }

    /// <summary>
    /// Declines the invitation.
    /// </summary>
    public ErrorOr<Success> Decline()
    {
        if (Status != InvitationStatus.Pending)
        {
            return Status switch
            {
                InvitationStatus.Accepted => OrganizationErrors.InvitationAlreadyAccepted,
                InvitationStatus.Declined => OrganizationErrors.InvitationAlreadyDeclined,
                InvitationStatus.Cancelled => OrganizationErrors.InvitationAlreadyCancelled,
                _ => Error.Validation(code: "Organization.InvitationNotPending",
                    description: "Only pending invitations can be declined.")
            };
        }

        Status = InvitationStatus.Declined;
        return Result.Success;
    }

    /// <summary>
    /// Cancels the invitation (by org admin).
    /// </summary>
    public ErrorOr<Success> Cancel()
    {
        if (Status != InvitationStatus.Pending)
        {
            return Status switch
            {
                InvitationStatus.Accepted => OrganizationErrors.InvitationAlreadyAccepted,
                InvitationStatus.Declined => OrganizationErrors.InvitationAlreadyDeclined,
                InvitationStatus.Cancelled => OrganizationErrors.InvitationAlreadyCancelled,
                _ => Error.Validation(code: "Organization.InvitationNotPending",
                    description: "Only pending invitations can be cancelled.")
            };
        }

        Status = InvitationStatus.Cancelled;
        return Result.Success;
    }

    /// <summary>
    /// Regenerates the invitation token and resets the expiration.
    /// Used when the invited user has lost their original invitation.
    /// </summary>
    public ErrorOr<Success> RegenerateToken(string newTokenHash, int expiresInDays = 7)
    {
        if (Status != InvitationStatus.Pending)
        {
            return Error.Validation(
                code: "Organization.InvitationNotPending",
                description: "Only pending invitations can be resent.");
        }

        TokenHash = newTokenHash;
        ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays);
        return Result.Success;
    }

    /// <summary>
    /// Marks the invitation as expired.
    /// </summary>
    public void MarkExpired()
    {
        if (Status == InvitationStatus.Pending)
        {
            Status = InvitationStatus.Expired;
        }
    }
}

/// <summary>
/// Represents the status of an organization invitation.
/// </summary>
public enum InvitationStatus
{
    /// <summary>
    /// Invitation is pending response.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Invitation was accepted.
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Invitation was declined.
    /// </summary>
    Declined = 2,

    /// <summary>
    /// Invitation expired before response.
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Invitation was cancelled by org admin.
    /// </summary>
    Cancelled = 4
}
