using Auth.Domain.Constants;
using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Errors for attaching a stored image to something that will display it.
/// </summary>
public static class ImageErrors
{
    /// <summary>
    /// The caller named a storage key it did not upload, or one already in use.
    /// </summary>
    /// <remarks>
    /// One error for both cases on purpose. Distinguishing them would answer
    /// "does this key exist and who owns it" for any key the caller cares to
    /// guess, and the two remedies are the same: upload the image again.
    /// <para>
    /// This matters more than a tidy contract, because the attach path deletes
    /// the key it replaces. Naming someone else's key and then changing your mind
    /// deleted their file, and possession of a key was the whole of the claim to
    /// it.
    /// </para>
    /// </remarks>
    public static Error NotAvailable => Error.Validation(
        code: "Image.NotAvailable",
        description: "That image is not available to attach. Upload the image again and retry.");

    public static Error DimensionsTooLarge(int maxMegapixels) => Error.Validation(
        code: "Image.DimensionsTooLarge",
        description: $"Image dimensions exceed the maximum of {maxMegapixels} megapixels.",
        metadata: new() { [ErrorMetadataKeys.Args] = new object[] { maxMegapixels } });

    public static readonly Error FileRequired = Error.Validation(
        code: "Image.FileRequired",
        description: "No file was provided.");

    public static readonly Error Invalid = Error.Validation(
        code: "Image.Invalid",
        description: "The file is not a valid image.");

    public static readonly Error StorageUnavailable = Error.Unexpected(
        code: "Image.StorageUnavailable",
        description: "Image storage is not available.");

    public static readonly Error UnsupportedType = Error.Validation(
        code: "Image.UnsupportedType",
        description: "This image type is not supported.");

    public static Error FileTooLarge(long maxSizeBytes) => Error.Validation(
        code: "Image.FileTooLarge",
        description: $"The file exceeds the maximum size of {maxSizeBytes} bytes.",
        metadata: new() { [ErrorMetadataKeys.Args] = new object[] { maxSizeBytes } });

    /// <summary>
    /// The uploader's images would exceed the per-user quota. The per-file limit
    /// bounds one request; this bounds their sum, so one account cannot fill the
    /// uploads volume a file at a time.
    /// </summary>
    public static Error QuotaExceeded(long usedBytes, long quotaBytes) => Error.Validation(
        code: "Image.QuotaExceeded",
        description: $"Storage quota reached: {usedBytes} of {quotaBytes} bytes used.",
        metadata: new() { [ErrorMetadataKeys.Args] = new object[] { usedBytes, quotaBytes } });
}
