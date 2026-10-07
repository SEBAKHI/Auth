using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Common;

/// <summary>
/// Enforces one rule for every writer that stores an image on an entity (the
/// application, organization and platform logos): <b>a stored upload key must be
/// one the actor uploaded.</b>
/// </summary>
/// <remarks>
/// <para>
/// A logo column used to accept any string up to its length limit, and creating
/// an organization is self-service, so any signed-in user could store another
/// user's upload key as their organization's logo. Claiming the key here also
/// attaches it in the uploads ledger, so the daily sweep keeps the file.
/// </para>
/// <para>
/// What passes without a ledger call: nothing (clears the logo); the value already
/// stored, compared raw and as a key, so a pre-ledger file or a legacy composed
/// URL never stops an entity from being saved; an external <c>https://</c> URL.
/// Everything else, an <c>http://</c> URL included, must be an upload key that
/// the actor can claim.
/// </para>
/// </remarks>
public class ImageReferenceGuard
{
    private readonly IUploadedImageRepository _uploadedImages;
    private readonly IImageUrlComposer _imageUrlComposer;

    public ImageReferenceGuard(
        IUploadedImageRepository uploadedImages,
        IImageUrlComposer imageUrlComposer)
    {
        _uploadedImages = uploadedImages;
        _imageUrlComposer = imageUrlComposer;
    }

    /// <summary>
    /// Confirms that <paramref name="incoming"/> may replace
    /// <paramref name="stored"/>, and claims it for <paramref name="actorId"/>
    /// when it is an upload key.
    /// </summary>
    /// <remarks>
    /// Call it before the entity is written. A claim followed by a failed write
    /// leaves an attached row that nothing references: a few kilobytes kept,
    /// never a file lost.
    /// </remarks>
    /// <param name="incoming">The value from the request, raw or composed.</param>
    /// <param name="stored">The value the entity holds now; null when creating.</param>
    /// <param name="actorId">The authenticated user writing the entity.</param>
    public async Task<ErrorOr<Success>> EnsureCanStoreAsync(
        string? incoming,
        string? stored,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var key = _imageUrlComposer.Decompose(incoming);
        if (key is null)
        {
            return Result.Success;
        }

        // Compared as keys, which also covers the raw comparison: equal values
        // decompose equally, and a composed URL equals the key it was built from.
        if (string.Equals(key, _imageUrlComposer.Decompose(stored), StringComparison.Ordinal))
        {
            return Result.Success;
        }

        if (key.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success;
        }

        return await _uploadedImages.TryClaimAsync(key, actorId, cancellationToken)
            ? Result.Success
            : ImageErrors.NotAvailable;
    }
}
