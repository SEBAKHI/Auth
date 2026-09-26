namespace Auth.Domain.Constants;

/// <summary>
/// Keys of <c>Error.Metadata</c> that the API's error mapper reads (ADR 0001).
/// Metadata never travels to the client as-is; the mapper takes exactly these.
/// </summary>
public static class ErrorMetadataKeys
{
    /// <summary>
    /// Values for the placeholders of the code's translated sentence, in order.
    /// </summary>
    public const string Args = "args";

    /// <summary>
    /// Property path of the request member a Validation error concerns
    /// (<c>Items[0].Name</c>). The mapper turns it into <c>errors[].pointer</c>.
    /// </summary>
    public const string Property = "property";
}
