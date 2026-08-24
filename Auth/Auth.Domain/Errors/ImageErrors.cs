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
}
