using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.DeactivateAccount;

/// <summary>
/// Handler for the deactivate account command.
/// </summary>
public class DeactivateAccountCommandHandler : IRequestHandler<DeactivateAccountCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly ILogger<DeactivateAccountCommandHandler> _logger;

    public DeactivateAccountCommandHandler(
        IUserRepository userRepository,
        ICredentialRevocationService credentialRevocation,
        ILogger<DeactivateAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _credentialRevocation = credentialRevocation;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        DeactivateAccountCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        user.Deactivate(request.DeactivatedBy);
        await _userRepository.UpdateAsync(user, cancellationToken);

        // Deactivation is the offboarding kill switch, so it has to end every way
        // back in, not just the session rows. Ending a UserSessions row evicts
        // nobody: the refresh grant reads that row only to touch LastActivity, so
        // a held refresh token walks the session straight back and mints a fresh
        // access token with the user's full authority — and rotation restarts the
        // whole refresh window each time, indefinitely.
        //
        // This REPLACES the direct TerminateAllForUserAsync rather than joining
        // it. Called after it, the enumeration inside this service would come
        // back empty, because it lists active sessions and the direct call has
        // already stamped EndedAt on all of them — nothing would be blacklisted
        // and the caller would still see a green result.
        await _credentialRevocation.RevokeAllCredentialsAsync(
            request.UserId,
            request.DeactivatedBy,
            "Account deactivated",
            cancellationToken);

        _logger.LogInformation(
            "User {UserId} account deactivated by {DeactivatedBy}",
            request.UserId,
            request.DeactivatedBy);

        return Result.Success;
    }
}
