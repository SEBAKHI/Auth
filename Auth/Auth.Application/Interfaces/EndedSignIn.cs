namespace Auth.Application.Interfaces;

/// <summary>
/// One sign-in a single logout ended (OI-155): its session, and the application that held it.
/// </summary>
/// <param name="SessionId">The session ended; null for a token issued without one, which was revoked alone.</param>
/// <param name="ApplicationId">The application whose sign-in it was; null for a platform app's own.</param>
public sealed record EndedSignIn(Guid? SessionId, Guid? ApplicationId);
