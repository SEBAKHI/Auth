namespace Auth_API.Common.RateLimiting;

/// <summary>
/// Marks the token endpoint (OI-178): before the rate limiter runs,
/// <see cref="Middleware.TokenClientClassificationMiddleware"/> reads the request's client
/// assertion and decides which budget it counts in (<see cref="TokenRequestClass"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassifiesTokenClientAttribute : Attribute
{
}
