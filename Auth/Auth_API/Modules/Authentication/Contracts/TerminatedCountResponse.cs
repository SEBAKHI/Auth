namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// How many sessions an operation ended.
///
/// A named type rather than an anonymous object: both endpoints that return this
/// previously declared <c>int</c> in their OpenAPI annotation while writing
/// <c>{ terminatedCount }</c> on the wire, so the generated client typed the
/// field out of existence.
/// </summary>
/// <param name="TerminatedCount">The number of sessions ended.</param>
public record TerminatedCountResponse(int TerminatedCount);
