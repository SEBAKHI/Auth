namespace Auth.Domain.Enums;

/// <summary>
/// The kind of token revocation stored in the durable blacklist.
/// </summary>
public enum RevocationType : byte
{
    /// <summary>A single access token, keyed by its jti.</summary>
    Token = 1,

    /// <summary>An entire login session, keyed by its sid.</summary>
    Session = 2,

    /// <summary>All of a user's tokens issued at or before a timestamp.</summary>
    User = 3
}
