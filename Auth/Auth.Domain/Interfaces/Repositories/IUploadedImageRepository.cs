namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The ledger for the uploads volume: who put each file there, how big it is,
/// and whether anything points at it yet.
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
    /// Returns the keys of uploads that were never attached and are older than
    /// <paramref name="olderThan"/>, and forgets them.
    /// </summary>
    /// <remarks>
    /// The caller deletes the files. Rows go first on purpose: a crash between
    /// the two leaves an unreferenced file, which the next sweep cannot see but
    /// which harms nothing, whereas the reverse order would leave a row pointing
    /// at a file that is gone.
    /// </remarks>
    Task<IReadOnlyList<string>> ReclaimUnattachedAsync(DateTime olderThan, CancellationToken cancellationToken);
}
