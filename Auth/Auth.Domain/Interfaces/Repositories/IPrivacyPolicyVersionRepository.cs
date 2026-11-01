using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Persistence port for the privacy-policy revision registry.
/// </summary>
public interface IPrivacyPolicyVersionRepository
{
    /// <summary>
    /// Gets every recorded revision, newest version first.
    /// </summary>
    Task<IReadOnlyList<PrivacyPolicyVersion>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets one revision by its "YYYY.MM" version, or null.
    /// </summary>
    Task<PrivacyPolicyVersion?> GetByVersionAsync(string version, CancellationToken cancellationToken);

    /// <summary>
    /// Gets one revision by its id, or null.
    /// </summary>
    Task<PrivacyPolicyVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the ids of every revision that has served documents: every revision that was
    /// published at least once, including those a later revision superseded.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetServedVersionIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether any account-deletion request or tombstone was stamped with this version.
    /// </summary>
    Task<bool> IsCitedByDeletionRecordsAsync(string version, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a draft revision and its written languages in one statement. Every condition of
    /// <see cref="PrivacyPolicyVersion.EnsureDeletable"/> and the citation check is repeated in
    /// the statement itself, so a revision published, announced or cited after the caller read it
    /// is not deleted. Returns false when nothing was deleted.
    /// </summary>
    /// <param name="id">The revision.</param>
    /// <param name="policyVersionInUse">The configured <c>AccountDeletion:PolicyVersion</c>.</param>
    Task<bool> TryDeleteDraftAsync(Guid id, string policyVersionInUse, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the revision; returns false when the version already exists
    /// (the unique index arbitrates the race).
    /// </summary>
    Task<bool> TryCreateAsync(PrivacyPolicyVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the notified-at/count stamped by
    /// <see cref="PrivacyPolicyVersion.MarkNotified"/>.
    /// </summary>
    Task UpdateNotifiedAsync(PrivacyPolicyVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the editable metadata (effective date, change note).
    /// </summary>
    Task UpdateDetailsAsync(PrivacyPolicyVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the published revision, or null when none is published yet.
    /// </summary>
    Task<PrivacyPolicyVersion?> GetPublishedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets every stored language document of a revision.
    /// </summary>
    Task<IReadOnlyList<PrivacyPolicyTranslation>> GetTranslationsAsync(
        Guid versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets one language document, or null when that language is unwritten.
    /// </summary>
    Task<PrivacyPolicyTranslation?> GetTranslationAsync(
        Guid versionId, string languageCode, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or replaces the document for one (version, language) pair.
    /// </summary>
    Task UpsertTranslationAsync(
        PrivacyPolicyTranslation translation, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a revision's whole set of served documents and makes that
    /// revision the published one in a single transaction.
    ///
    /// All-or-nothing on purpose: a partial set would leave some languages on
    /// the previous revision's text while others moved, which is a version skew
    /// across locales rather than a slow rollout.
    /// </summary>
    Task PublishArtifactsAsync(
        Guid versionId,
        IReadOnlyList<PrivacyPolicyArtifact> artifacts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the served document for a revision and language, or null when that
    /// revision has never been published.
    /// </summary>
    Task<PrivacyPolicyArtifact?> GetArtifactAsync(
        Guid versionId, string languageCode, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the served document of the currently published revision — a single
    /// round-trip, because this is the public read path.
    /// </summary>
    Task<PrivacyPolicyArtifact?> GetPublishedArtifactAsync(
        string languageCode, CancellationToken cancellationToken);
}
