namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The ledger for the uploads volume: who put each file there, how big it is,
/// and whether anything points at it yet (see <see cref="ReclaimUnattachedAsync"/>
/// for what "unattached" means).
/// </summary>
/// <remarks>
/// The filesystem cannot answer any of those. Upload and attach are separate
/// calls, so without this the volume held files nobody owned, nobody was
/// counting, and nobody would ever come back for.
/// </remarks>
public interface IUploadedImageRepository
{
    /// <summary>
    /// Records a file that has just been written to storage, unattached.
    /// </summary>
    Task RecordAsync(string storageKey, Guid uploadedBy, long sizeBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Total bytes this user currently occupies, attached or not.
    /// </summary>
    Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a key as in use, but only if <paramref name="userId"/> uploaded it
    /// and nothing has claimed it yet. Returns false otherwise.
    /// </summary>
    /// <remarks>
    /// This is the ownership check. Attaching used to accept any key the caller
    /// could name, and the attach path deletes the key it replaces — so naming
    /// somebody else's key and then changing your mind deleted their file.
    /// Returning false rather than throwing keeps the caller in charge of which
    /// error its own contract should produce.
    /// </remarks>
    Task<bool> TryAttachAsync(string storageKey, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a key as in use when <paramref name="userId"/> uploaded it, whether
    /// or not something already uses it. Returns false when the key is unknown or
    /// somebody else uploaded it.
    /// </summary>
    /// <remarks>
    /// The ownership check for a writer that stores a key on an entity (a logo).
    /// Unlike <see cref="TryAttachAsync"/> it accepts a key that is already
    /// attached, so one upload may fill two slots of its uploader, and saving the
    /// same key twice passes. An attach that is followed by a failed entity write
    /// leaves an attached row that nothing references: a few kilobytes kept,
    /// never a file lost.
    /// </remarks>
    Task<bool> TryClaimAsync(string storageKey, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Sweeps the next batch of unattached uploads, those whose keys sort after
    /// <paramref name="after"/>: attaches the ones something references, then
    /// forgets the ones older than <paramref name="olderThan"/> that nothing
    /// references, and returns their keys. Null when no unattached upload sorts
    /// after <paramref name="after"/>.
    /// </summary>
    /// <remarks>
    /// "Unattached" means unattached AND referenced by nothing. Only the profile
    /// image and the logo writers attach a key when they store it; a template or
    /// layout body holds an image URL in its HTML and attaches nothing. So the
    /// sweep, the one place that deletes an upload nobody chose to remove, checks
    /// every column that can hold an upload before it reclaims one, and keeps a
    /// referenced upload for good: mail already delivered points at template
    /// images.
    /// <para>
    /// The caller walks the batches: an empty string starts, and each result's
    /// <see cref="UploadSweepBatch.Next"/> is the following
    /// <paramref name="after"/>. It deletes a batch's files before it asks for
    /// the next batch. Rows go first on purpose: a crash between the two leaves
    /// an unreferenced file, which the next sweep cannot see but which harms
    /// nothing, whereas the reverse order would leave a row pointing at a file
    /// that is gone. Deleting per batch keeps that leftover to one batch.
    /// </para>
    /// </remarks>
    Task<UploadSweepBatch?> SweepBatchAsync(string after, DateTime olderThan, CancellationToken cancellationToken);
}

/// <summary>
/// What one batch of the uploads sweep did.
/// </summary>
/// <param name="Next">The last key the batch covered; pass it as the next batch's start.</param>
/// <param name="Adopted">Unattached uploads found referenced, and now attached.</param>
/// <param name="Reclaimed">Keys of the uploads forgotten; the caller deletes their files.</param>
public sealed record UploadSweepBatch(string Next, int Adopted, IReadOnlyList<string> Reclaimed);
