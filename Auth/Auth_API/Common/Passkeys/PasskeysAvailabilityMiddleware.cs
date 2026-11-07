using Auth.Application.Features.Passkeys.Common;

namespace Auth_API.Common.Passkeys;

/// <summary>
/// Marks an endpoint that exists only while passkeys run (S20, PK19).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class PasskeysEndpointAttribute : Attribute
{
}

/// <summary>
/// Answers 404 for every passkey endpoint while passkeys do not run (PK19): switched off, or
/// switched on without an RP ID, a first-party origin or the schema. Before authentication and
/// authorization, so an endpoint of a feature that is off answers exactly as an address that
/// does not exist — for a signed-in caller and an anonymous one alike.
/// </summary>
/// <remarks>
/// The switch is read per request (hot). The same assessment decides whether a passkey can be
/// used as a factor (D-150-4, <c>UsableSecondFactors</c>): while it answers 404 here, no policy
/// demands a passkey or leans on one. The passkeys stay in the inventory (FA2), and nothing is
/// revoked.
/// </remarks>
public sealed class PasskeysAvailabilityMiddleware
{
    private readonly RequestDelegate _next;

    public PasskeysAvailabilityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, PasskeyFeature feature)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<PasskeysEndpointAttribute>() is not null
            && !(await feature.AssessAsync(context.RequestAborted)).IsAvailable)
        {
            // An empty 404: the error contract writes the same problem body an unknown
            // address gets (Http.NotFound).
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }
}
