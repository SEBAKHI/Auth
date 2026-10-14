namespace Auth.Application.Interfaces;

/// <summary>
/// Whether the sign-in being answered came from a browser page (OI-154, owner
/// decision D-74-3 (a)). Only a browser sign-in is tied to the IAM session it
/// mints: its refresh tokens stop once that session ends. A server, the SDK or a
/// script keeps the refresh behaviour it always had.
/// </summary>
public interface IBrowserSignInContext
{
    /// <summary>
    /// Gets whether the current request carries an <c>Origin</c> header. Every
    /// browser sends one on a sign-in POST; a server, the SDK and scripts do not.
    /// </summary>
    bool IsBrowserSignIn { get; }
}
