using Auth.Application.Configuration;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.AccountDeletion.Common;

/// <summary>
/// The shared deletion-request pipeline behind both entry points (in-app and
/// public web): guards, request row, immediate soft-deactivation, full
/// credential revocation and the requested event. Callers re-authenticate the
/// user BEFORE invoking this.
/// </summary>
public class AccountDeletionRequestor
{
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly IUserRepository _userRepository;
    private readonly OwnedOrganizationDeletionGuard _organizationGuard;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly AccountDeletionSettings _settings;
    private readonly ILogger<AccountDeletionRequestor> _logger;

    public AccountDeletionRequestor(
        IAccountDeletionRequestRepository requestRepository,
        IUserRepository userRepository,
        OwnedOrganizationDeletionGuard organizationGuard,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        IOptionsSnapshot<AccountDeletionSettings> settings,
        ILogger<AccountDeletionRequestor> logger)
    {
        _requestRepository = requestRepository;
        _userRepository = userRepository;
        _organizationGuard = organizationGuard;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Starts the two-phase deletion for an already re-authenticated user:
    /// creates the grace-period request, hides the account everywhere
    /// (IsDeleted), revokes every session/token/SSO cookie and publishes
    /// <see cref="AccountDeletionRequestedEvent"/>.
    /// </summary>
    public async Task<ErrorOr<AccountDeletionRequest>> RequestAsync(
        User user, AccountDeletionSource source, CancellationToken cancellationToken)
    {
        // The well-known id check is the effective system guard: no query
        // populates IsSystemUser (the Users table has no such column).
        if (user.IsSystemUser || user.Id == WellKnownUserIds.System)
        {
            return UserErrors.CannotDeleteSystemUser;
        }

        if (await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken) is not null)
        {
            return UserErrors.DeletionAlreadyRequested;
        }

        // Shared owned-organization rule: block while an owned organization
        // still has other members; delete sole-member owned ones with the account.
        var organizationsResult = await _organizationGuard.EnsureDeletableAsync(user.Id, cancellationToken);
        if (organizationsResult.IsError)
        {
            return organizationsResult.Errors;
        }

        var request = AccountDeletionRequest.Create(
            user.Id, source, _settings.GracePeriod, _settings.PolicyVersion, user.Id);

        // The filtered unique index is the source of truth for "one active
        // request per user"; losing the insert race means someone else won.
        if (!await _requestRepository.TryCreateAsync(request, cancellationToken))
        {
            return UserErrors.DeletionAlreadyRequested;
        }

        // Immediate deactivation: hidden everywhere, login blocked (R1) —
        // then log the account out of everything, everywhere (R7).
        await _userRepository.DeleteAsync(user.Id, cancellationToken);
        await _credentialRevocation.RevokeAllCredentialsAsync(
            user.Id, user.Id, "Account deletion requested", cancellationToken);

        _logger.LogInformation(
            "Account deletion requested for user {UserId} via {Source}; grace ends {GraceEndsAtUtc}",
            user.Id, source, request.GraceEndsAtUtc);

        await _publisher.Publish(
            new AccountDeletionRequestedEvent(
                user.Id, user.Email, DisplayNameOf(user), source, request.GraceEndsAtUtc),
            cancellationToken);

        return request;
    }

    /// <summary>
    /// The display name used in deletion notifications, mirroring the
    /// recipient-name convention of the other notification flows.
    /// </summary>
    public static string DisplayNameOf(User user) => user.DisplayName ?? user.FirstName ?? "User";
}
