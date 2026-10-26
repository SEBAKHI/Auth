using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
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
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly OwnedOrganizationDeletionGuard _organizationGuard;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly AccountDeletionSettings _settings;
    private readonly ILogger<AccountDeletionRequestor> _logger;

    public AccountDeletionRequestor(
        IAccountDeletionRequestRepository requestRepository,
        IUserRepository userRepository,
        AccountActionPolicy accountActionPolicy,
        OwnedOrganizationDeletionGuard organizationGuard,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        IOptionsSnapshot<AccountDeletionSettings> settings,
        ILogger<AccountDeletionRequestor> logger)
    {
        _requestRepository = requestRepository;
        _userRepository = userRepository;
        _accountActionPolicy = accountActionPolicy;
        _organizationGuard = organizationGuard;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// The deterministic, side-effect-free reasons a deletion request would be
    /// refused: the system account, the last full administrator, one already in
    /// flight, or an owned organization that still has other members. A caller about to consume a
    /// single-use re-authentication code runs this FIRST, so a foreseeable
    /// conflict refuses the request without burning the user's code.
    /// <para>
    /// Only for callers that have already identified the account by some means
    /// other than the code — these errors reveal account state, so an anonymous
    /// caller must prove email possession before it may learn any of them.
    /// </para>
    /// </summary>
    public async Task<ErrorOr<Success>> EnsureRequestableAsync(
        User user, CancellationToken cancellationToken)
    {
        var allowed = await _accountActionPolicy.EnsureAllowedAsync(
            new AccountActionRequest(user.Id, user.Id, AccountAction.RequestOwnDeletion), cancellationToken);
        if (allowed.IsError)
        {
            return allowed.Errors;
        }

        if (await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken) is not null)
        {
            return UserErrors.DeletionAlreadyRequested;
        }

        return await _organizationGuard.CheckBlockingAsync(user.Id, cancellationToken);
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
        // Re-checked here even when a caller already ran EnsureRequestableAsync: that
        // precheck exists to save the user's code, not to be trusted as the gate. This
        // is the authoritative evaluation, inside the operation: the system account,
        // and the last full administrator, judged under the lock the soft delete below
        // runs under — leaving the platform without one is refused even by one's own
        // act, from the console or the public page.
        var begun = await _accountActionPolicy.BeginAsync(
            new AccountActionRequest(user.Id, user.Id, AccountAction.RequestOwnDeletion), cancellationToken);
        if (begun.IsError)
        {
            return begun.Errors;
        }

        AccountDeletionRequest request;
        await using (IAccountAuthorityChange change = begun.Value)
        {
            if (await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken) is not null)
            {
                return UserErrors.DeletionAlreadyRequested;
            }

            // Shared owned-organization rule: block while an owned organization
            // still has other members; delete sole-member owned ones with the account.
            // Neither this nor the request insert below is part of the locked
            // transaction: each runs on its own connection, committed at once. If the
            // soft delete then fails, the sole-member organizations stay deleted and the
            // request stands while the account is not hidden: a retry answers
            // DeletionAlreadyRequested, and the grace-period worker executes the request
            // when the grace ends (OI-225 item 10).
            var organizationsResult = await _organizationGuard.EnsureDeletableAsync(user.Id, cancellationToken);
            if (organizationsResult.IsError)
            {
                return organizationsResult.Errors;
            }

            request = AccountDeletionRequest.Create(
                user.Id, source, _settings.GracePeriod, _settings.PolicyVersion, user.Id);

            // The filtered unique index is the source of truth for "one active
            // request per user"; losing the insert race means someone else won.
            if (!await _requestRepository.TryCreateAsync(request, cancellationToken))
            {
                return UserErrors.DeletionAlreadyRequested;
            }

            // Immediate deactivation: hidden everywhere, login blocked (R1) —
            // then log the account out of everything, everywhere (R7).
            await change.DeleteAsync(user.Id, cancellationToken);
            change.Commit();
        }

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
    public static string DisplayNameOf(User user) => user.NameToShow;
}
