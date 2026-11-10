using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Reauthenticate;

/// <summary>What the signed-in account must prove to prove itself again: what the window asks for.</summary>
/// <param name="UserId">The authenticated user.</param>
/// <param name="CurrentSessionId">The session the access token belongs to.</param>
public record GetReauthenticationOptionsQuery(Guid UserId, Guid? CurrentSessionId)
    : IRequest<ErrorOr<ReauthenticationOptionsResponse>>;

/// <summary>What a signed-in account must prove to prove itself again.</summary>
/// <param name="Passkey">A passkey proves the person, alone.</param>
/// <param name="Password">The password is the first proof.</param>
/// <param name="EmailCode">An emailed code is the first proof.</param>
/// <param name="SecondStep">
/// What must follow the password or the emailed code: a name of <see cref="ReauthenticationSecondStep"/>,
/// as every enum in a body travels, and typed so in the published schema.
/// </param>
/// <param name="CanProveInPlace">False when nothing can be proved without a new sign-in.</param>
public sealed record ReauthenticationOptionsResponse(
    bool Passkey,
    bool Password,
    bool EmailCode,
    string SecondStep,
    bool CanProveInPlace);

/// <summary>Handler for <see cref="GetReauthenticationOptionsQuery"/>.</summary>
public sealed class GetReauthenticationOptionsQueryHandler
    : IRequestHandler<GetReauthenticationOptionsQuery, ErrorOr<ReauthenticationOptionsResponse>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IUserRepository _userRepository;
    private readonly ReauthenticationPolicy _policy;

    public GetReauthenticationOptionsQueryHandler(
        IReauthenticationGuard reauthenticationGuard,
        IUserRepository userRepository,
        ReauthenticationPolicy policy)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _userRepository = userRepository;
        _policy = policy;
    }

    public async Task<ErrorOr<ReauthenticationOptionsResponse>> Handle(
        GetReauthenticationOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var session = await _reauthenticationGuard.EnsureLiveSessionAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.ReauthenticationRequired;
        }

        var requirement = await _policy.ForAsync(user, cancellationToken);

        return new ReauthenticationOptionsResponse(
            requirement.Passkey,
            requirement.Password,
            requirement.EmailCode,
            requirement.SecondStep.ToString(),
            requirement.CanProveInPlace);
    }
}
