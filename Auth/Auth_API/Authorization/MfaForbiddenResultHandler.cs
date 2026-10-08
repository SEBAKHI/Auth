using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth_API.Common.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Auth_API.Authorization;

/// <summary>
/// Names the reason when a platform administrator is refused for want of a
/// second factor: a forbidden result on a token that carries <c>mfa_req</c>
/// answers 403 <c>TwoFactor.RequiredByPolicy</c> with
/// <c>WWW-Authenticate: Bearer error="insufficient_user_authentication"</c>
/// (RFC 9470 §3). Every other result is handled exactly as before.
/// </summary>
/// <remarks>
/// <para>
/// 403, where RFC 9470's example uses 401, on purpose: the consoles refresh their
/// token on 401, and a refresh mints the same withheld token again, so a 401 would
/// loop. The clients key on the code, never on the status — other refusals are 403
/// too (<c>TwoFactor.LockedOut</c>, <c>Auth.ReauthenticationRequired</c>).
/// </para>
/// <para>
/// The claim is present only on a token whose platform permissions and roles were
/// withheld, which an application token never is: for every other caller this
/// handler changes nothing, the OIDC userinfo endpoint included.
/// </para>
/// </remarks>
public sealed class MfaForbiddenResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The challenge a refusal for want of a second factor carries (RFC 9470 §3).</summary>
    public const string InsufficientUserAuthentication =
        "Bearer error=\"insufficient_user_authentication\", error_description=\"A second factor is required\"";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.HasClaim(c => c.Type == JwtClaimNames.MfaRequirement))
        {
            context.Response.Headers.WWWAuthenticate = InsufficientUserAuthentication;

            // The status and the code only; the status-code pages write the
            // problem body, as for every refusal before an action runs.
            ProblemMapping.Reject(context, TwoFactorErrors.RequiredByPolicy);
            return Task.CompletedTask;
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
