namespace Auth.Application.Features.Discovery.GetDiscoveryDocument;

/// <summary>
/// The endpoint addresses the discovery document publishes, built in one place so that what an
/// API answer tells a client (a machine key's token endpoint) is what discovery tells it.
/// </summary>
public static class DiscoveryEndpoints
{
    private const string ApiVersion = "v1";

    /// <summary>The OAuth token endpoint under <paramref name="baseUrl"/>.</summary>
    public static string Token(string baseUrl) => $"{baseUrl}/api/{ApiVersion}/auth/token";
}
