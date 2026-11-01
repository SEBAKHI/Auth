using Auth.Domain.Constants;
using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Errors for the platform's font files (heading and body).
/// </summary>
public static class FontErrors
{
    public static readonly Error FileRequired = Error.Validation(
        code: "Font.FileRequired",
        description: "No font file was provided.");

    /// <summary>
    /// Not a WOFF2 font, or one that is damaged: a wrong signature, a declared length that is
    /// not the file's, a table directory that does not parse, or font data that does not
    /// decompress to what the directory declares.
    /// </summary>
    public static readonly Error Invalid = Error.Validation(
        code: "Font.Invalid",
        description: "The file is not a valid WOFF2 font.");

    public static readonly Error StorageUnavailable = Error.Unexpected(
        code: "Font.StorageUnavailable",
        description: "Font storage is not available.");

    public static Error FileTooLarge(long maxSizeBytes) => Error.Validation(
        code: "Font.FileTooLarge",
        description: $"The font file exceeds the maximum size of {maxSizeBytes} bytes.",
        metadata: new() { [ErrorMetadataKeys.Args] = new object[] { maxSizeBytes } });
}
