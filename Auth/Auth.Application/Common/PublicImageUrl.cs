using Auth.Application.Interfaces;

namespace Auth.Application.Common;

/// <summary>
/// The address of a stored image as a relying party may receive it: an absolute http(s) URL
/// with no credentials in it, or nothing. A relative address (the default image base) means
/// nothing to a server on another host, and a URL carrying a user name or password is never
/// handed out. One rule for every answer an application reads (UserInfo, the application
/// directory), so the two cannot disagree about the same picture.
/// </summary>
public static class PublicImageUrl
{
    /// <summary>
    /// Composes <paramref name="keyOrUrl"/> and returns it only when the result is an absolute
    /// http(s) URL without user information; otherwise null.
    /// </summary>
    public static string? ComposeAbsolute(this IImageUrlComposer composer, string? keyOrUrl) =>
        Uri.TryCreate(composer.Compose(keyOrUrl), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.UserInfo.Length == 0
            ? uri.AbsoluteUri
            : null;
}
