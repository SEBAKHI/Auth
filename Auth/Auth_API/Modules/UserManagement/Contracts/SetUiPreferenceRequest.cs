namespace Auth_API.Modules.UserManagement.Contracts;

/// <summary>
/// Stores one client display preference. The key travels in the route; the
/// value is an opaque JSON document the server never interprets.
/// </summary>
public record SetUiPreferenceRequest(string Value);
