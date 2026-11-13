namespace Auth.Application.Interfaces;

/// <summary>
/// The current request's <c>Origin</c> header, for the bot check's verdict line (S13, BD12): the
/// pass rate is read per app. Null outside a request, or when the request has none.
/// </summary>
public interface IRequestOriginContext
{
    /// <summary>The header's value as sent, or null.</summary>
    string? Origin { get; }
}
