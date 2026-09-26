using Auth.Shared.Http.ErrorContract;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Common.Errors;

/// <summary>
/// The JwtBearer challenge's contribution to the error contract (ADR 0001): an expired access
/// token is answered with <see cref="ChallengeReasonCodes.TokenExpired"/>, so a client refreshes
/// instead of signing in again. The handler still writes WWW-Authenticate; the status-code pages
/// write the body.
/// </summary>
public static class JwtChallengeReasons
{
    public static Task Record(JwtBearerChallengeContext context)
    {
        if (context.AuthenticateFailure is SecurityTokenExpiredException)
        {
            context.HttpContext.Items[ProblemItems.Code] = ChallengeReasonCodes.TokenExpired;
        }

        return Task.CompletedTask;
    }
}
