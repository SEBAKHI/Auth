using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// An invitation: this user may sign in to this application. Read only while the
/// application is in <see cref="Enums.ApplicationAccessMode.Restricted"/> mode —
/// an application open to everyone never consults the invitation list, which is
/// why the list only ever holds people an administrator added by hand.
/// <para>
/// The invitation opens the door and nothing more. Whatever the user may do once
/// inside comes from their roles and permissions; a grant on its own yields an
/// application-scoped token with no authority.
/// </para>
/// </summary>
public class ApplicationUserAccess : AuditableEntityBase
{
    /// <summary>
    /// Gets the application this invitation is for.
    /// </summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>
    /// Gets the invited user.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets whether the invitation currently stands. Revoking sets this false
    /// rather than deleting the row, so a past trial stays on the record.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets when the invitation was issued (or last reinstated).
    /// </summary>
    public DateTime GrantedAt { get; private set; }

    /// <summary>
    /// Gets the administrator who issued the invitation.
    /// </summary>
    public Guid GrantedBy { get; private set; }

    /// <summary>
    /// Gets the optional expiry. A trial invitation given an expiry lapses on
    /// its own, with nobody having to remember it.
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    /// <summary>
    /// Gets when the invitation was revoked, if it was.
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// Gets the administrator who revoked the invitation, if it was revoked.
    /// </summary>
    public Guid? RevokedBy { get; private set; }

    /// <summary>
    /// Gets the optional free-text reason recorded with the invitation.
    /// </summary>
    public string? Note { get; private set; }

    private ApplicationUserAccess() : base()
    {
    }

    public ApplicationUserAccess(
        Guid id,
        Guid applicationId,
        Guid userId,
        bool isActive,
        DateTime grantedAt,
        Guid grantedBy,
        DateTime? expiresAt,
        DateTime? revokedAt,
        Guid? revokedBy,
        string? note,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        ApplicationId = applicationId;
        UserId = userId;
        IsActive = isActive;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        Note = note;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    public static ApplicationUserAccess Create(
        Guid applicationId,
        Guid userId,
        Guid grantedBy,
        DateTime? expiresAt = null,
        string? note = null)
    {
        var access = new ApplicationUserAccess
        {
            ApplicationId = applicationId,
            UserId = userId,
            IsActive = true,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = grantedBy,
            ExpiresAt = expiresAt,
            Note = note
        };
        access.SetCreated(grantedBy);
        return access;
    }

    /// <summary>
    /// Checks whether the invitation admits the user right now.
    /// </summary>
    public bool IsValid()
    {
        return IsActive && RevokedAt is null && !IsExpired();
    }

    /// <summary>
    /// Checks whether the invitation has lapsed on its own.
    /// </summary>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    }

    /// <summary>
    /// Withdraws the invitation without erasing it.
    /// </summary>
    public void Revoke(Guid revokedBy)
    {
        IsActive = false;
        RevokedAt = DateTime.UtcNow;
        RevokedBy = revokedBy;
        SetModified(revokedBy);
    }

    /// <summary>
    /// Re-invites someone previously revoked or expired, on the same row, so the
    /// unique (application, user) constraint holds and the earlier trial stays
    /// visible in the audit trail.
    /// </summary>
    public void Reinstate(Guid grantedBy, DateTime? expiresAt = null, string? note = null)
    {
        IsActive = true;
        GrantedAt = DateTime.UtcNow;
        GrantedBy = grantedBy;
        ExpiresAt = expiresAt;
        RevokedAt = null;
        RevokedBy = null;
        Note = note;
        SetModified(grantedBy);
    }
}
