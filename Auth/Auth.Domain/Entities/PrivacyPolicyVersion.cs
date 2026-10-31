using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// One published revision of the privacy policy (format "YYYY.MM"). The
/// registry is the compliance record of when each revision took effect and
/// when (and to how many recipients) the change notification was sent —
/// backing the policy's own "we notify you of material changes" promise.
/// </summary>
public class PrivacyPolicyVersion : EntityBase
{
    /// <summary>
    /// Gets the version identifier in "YYYY.MM" format. Unique.
    /// </summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC instant this revision takes (or took) effect.
    /// </summary>
    public DateTime EffectiveDateUtc { get; private set; }

    /// <summary>
    /// Gets whether this revision is the one served to end users. Exactly one
    /// version is published at a time; the others are drafts or history.
    /// </summary>
    public bool IsPublished { get; private set; }

    /// <summary>
    /// Gets the editor's note describing what changed in this revision.
    /// </summary>
    public string? ChangeNote { get; private set; }

    /// <summary>
    /// Gets when the change notification was sent to active users; null while
    /// no notification has been sent for this revision.
    /// </summary>
    public DateTime? NotifiedAtUtc { get; private set; }

    /// <summary>
    /// Gets how many recipients the change notification reached; null while
    /// no notification has been sent.
    /// </summary>
    public int? NotifiedCount { get; private set; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets the admin who recorded the revision.
    /// </summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>
    /// Gets when this revision was first published; null while nobody has been shown it.
    /// Never cleared: a revision a later one superseded is back to
    /// <see cref="IsPublished"/> false but stays published once.
    /// </summary>
    public DateTime? FirstPublishedAtUtc { get; private set; }

    private PrivacyPolicyVersion() : base()
    {
    }

    public PrivacyPolicyVersion(
        Guid id,
        string version,
        DateTime effectiveDateUtc,
        bool isPublished,
        string? changeNote,
        DateTime? notifiedAtUtc,
        int? notifiedCount,
        DateTime createdAt,
        Guid createdBy,
        DateTime? firstPublishedAtUtc = null) : base(id)
    {
        FirstPublishedAtUtc = firstPublishedAtUtc;
        Version = version;
        EffectiveDateUtc = effectiveDateUtc;
        IsPublished = isPublished;
        ChangeNote = changeNote;
        NotifiedAtUtc = notifiedAtUtc;
        NotifiedCount = notifiedCount;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <summary>
    /// Records a new policy revision.
    /// </summary>
    public static PrivacyPolicyVersion Create(
        string version,
        DateTime effectiveDateUtc,
        string? changeNote,
        Guid createdBy)
    {
        return new PrivacyPolicyVersion
        {
            Version = version,
            EffectiveDateUtc = effectiveDateUtc,
            ChangeNote = changeNote,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    /// <summary>
    /// Renames the revision. Callers MUST have verified the revision is an
    /// unannounced draft: a published or notified version's identifier is
    /// referenced by deletion records and by users' inboxes.
    /// </summary>
    public void Rename(string version)
    {
        Version = version;
    }

    /// <summary>Updates the revision's editable metadata.</summary>
    public void UpdateDetails(DateTime effectiveDateUtc, string? changeNote)
    {
        EffectiveDateUtc = effectiveDateUtc;
        ChangeNote = changeNote;
    }

    /// <summary>
    /// Records that the change notification went out to
    /// <paramref name="recipientCount"/> active users just now.
    /// </summary>
    public void MarkNotified(int recipientCount)
    {
        NotifiedAtUtc = DateTime.UtcNow;
        NotifiedCount = recipientCount;
    }

    /// <summary>
    /// Marks this revision as the published one. The repository clears the
    /// flag from every other row in the same transaction.
    /// </summary>
    public void Publish()
    {
        IsPublished = true;
        FirstPublishedAtUtc ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Whether this revision may be deleted: only a draft nobody was ever shown or told about.
    /// Everything else is legal evidence and stays. Refused when the revision is published,
    /// was published once (<see cref="FirstPublishedAtUtc"/>, or served documents of it exist:
    /// a later revision may have superseded it), when its change notice went out, or when it
    /// is the version account deletions are stamped with today
    /// (<paramref name="policyVersionInUse"/>).
    /// </summary>
    /// <param name="hasServedDocuments">Whether any served document of this revision exists.</param>
    /// <param name="policyVersionInUse">The configured <c>AccountDeletion:PolicyVersion</c>.</param>
    public ErrorOr<Success> EnsureDeletable(bool hasServedDocuments, string policyVersionInUse) =>
        IsPublished
        || FirstPublishedAtUtc is not null
        || NotifiedAtUtc is not null
        || hasServedDocuments
        || string.Equals(Version, policyVersionInUse, StringComparison.Ordinal)
            ? PrivacyPolicyErrors.NotDeletable(Version)
            : Result.Success;
}
