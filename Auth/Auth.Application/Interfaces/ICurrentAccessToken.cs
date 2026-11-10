namespace Auth.Application.Interfaces;

/// <summary>
/// The access token the current request was authenticated with, as far as a check needs it: when it
/// was issued. The API reads it from the request's validated principal, so no command carries it.
/// </summary>
public interface ICurrentAccessToken
{
    /// <summary>
    /// When the request's access token was issued: its <c>iat</c>, in whole seconds, UTC. Null outside
    /// a request, or for a token that names no time.
    /// </summary>
    DateTime? IssuedAtUtc { get; }
}
