using Auth.Application.Features.Authentication.ClassifyTokenClient;
using Auth_API.Common.RateLimiting;
using MediatR;
using Microsoft.Extensions.Primitives;

namespace Auth_API.Common.Middleware;

/// <summary>
/// Decides, before the rate limiter, which budget a token request counts in (OI-178): its
/// application's, when the request carries a client assertion that verifies; its address's
/// otherwise. Runs only on endpoints marked <see cref="ClassifiesTokenClientAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// A rate-limit partition is chosen synchronously, before the action runs, and only verifying
/// the assertion tells a real application from someone naming it. Counting on the claimed
/// <c>client_id</c> instead would let anyone empty an application's budget with junk
/// assertions that name it, and sign its people out.
/// </para>
/// <para>
/// Nothing is spent here: the assertion's jti is only read, and the grant that runs after the
/// limiter spends it, verifying nothing twice (the authenticator keeps its answer for the
/// request). A form that cannot be read is left to model binding, which answers it as before.
/// </para>
/// </remarks>
public sealed class TokenClientClassificationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TokenAssertionFailureBudget _failures;

    public TokenClientClassificationMiddleware(RequestDelegate next, TokenAssertionFailureBudget failures)
    {
        _next = next;
        _failures = failures;
    }

    public async Task InvokeAsync(HttpContext context, ISender sender)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ClassifiesTokenClientAttribute>() is not null
            && context.Request.HasFormContentType)
        {
            (await ClassifyAsync(context, sender)).MarkOn(context);
        }

        await _next(context);
    }

    private async Task<TokenRequestClass> ClassifyAsync(HttpContext context, ISender sender)
    {
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return TokenRequestClass.ByAddress;
        }

        // As model binding reads them, so the grant asks the authenticator the same question.
        string? Field(string name) => form[name] is var value && !StringValues.IsNullOrEmpty(value)
            && !string.IsNullOrWhiteSpace(value.ToString())
                ? value.ToString()
                : null;

        var assertionType = Field("client_assertion_type");
        var assertion = Field("client_assertion");
        if (assertionType is null && assertion is null)
        {
            return TokenRequestClass.ByAddress;
        }

        var clientAddress = ClientIpResolver.Resolve(context) ?? "unknown";
        if (!_failures.HasRoom(clientAddress))
        {
            return TokenRequestClass.Refused;
        }

        var verified = await sender.Send(
            new ClassifyTokenClientQuery(Field("grant_type"), assertionType, assertion, Field("client_id"), ClientIpResolver.Resolve(context)),
            context.RequestAborted);
        if (verified.IsError)
        {
            _failures.Spend(clientAddress);
            return TokenRequestClass.ByAddress;
        }

        return TokenRequestClass.ForApplication(verified.Value);
    }
}
