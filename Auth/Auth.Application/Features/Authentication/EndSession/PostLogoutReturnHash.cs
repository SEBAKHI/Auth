using System.Security.Cryptography;
using System.Text;

namespace Auth.Application.Features.Authentication.EndSession;

/// <summary>
/// How an accounts page names a registered post-logout redirect URI without
/// carrying it: the lowercase hex SHA-256 of the URI, in the internal query
/// parameter <see cref="Parameter"/>.
/// </summary>
/// <remarks>
/// A web application firewall may refuse a page whose query holds a whole
/// <c>https://</c> address (it reads it as a remote file inclusion), so no
/// accounts URL ever carries the address itself. Only the end-session endpoint
/// receives <c>post_logout_redirect_uri</c>, from the relying party, because
/// the protocol says so. The hash names nothing by itself: it is honoured only
/// when it matches an address registered for the application in
/// <c>client_id</c>, so it can never point anywhere that was not registered.
/// </remarks>
public static class PostLogoutReturnHash
{
    /// <summary>
    /// The query parameter carrying the hash, between the accounts logout page
    /// and the end-session endpoint. Internal to this provider: a relying party
    /// sends <c>post_logout_redirect_uri</c>, never this.
    /// </summary>
    public const string Parameter = "plr";

    /// <summary>The hash of one registered URI, as the accounts page carries it.</summary>
    public static string Of(string uri) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));
}
