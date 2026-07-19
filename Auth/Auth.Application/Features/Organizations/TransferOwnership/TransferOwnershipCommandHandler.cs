using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.TransferOwnership;

/// <summary>
/// Handler for completing an organization ownership transfer. The owner path
/// verifies the one-time code emailed to the new owner (two-party consent);
/// the PlatformScope path transfers directly as the recovery valve. The
/// OwnerId column and both membership roles change in one atomic operation so
/// the "exactly one owner" invariant can never be observed broken.
/// </summary>
public class TransferOwnershipCommandHandler : IRequestHandler<TransferOwnershipCommand, ErrorOr<Success>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IOwnershipTransferCodeRepository _transferCodeRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPublisher _publisher;
    private readonly ILogger<TransferOwnershipCommandHandler> _logger;

    public TransferOwnershipCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IOwnershipTransferCodeRepository transferCodeRepository,
        IPasswordHasher passwordHasher,
        IPublisher publisher,
        ILogger<TransferOwnershipCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _transferCodeRepository = transferCodeRepository;
        _passwordHasher = passwordHasher;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        TransferOwnershipCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        var isOwner = organization.OwnerId == request.RequestedBy;
        if (!isOwner && !request.PlatformScope)
        {
            return OrganizationErrors.NotOwner;
        }

        // Personal organizations are bound to their account and never change
        // hands — absolute, even for platform administrators.
        if (organization.IsAutoCreated)
        {
            return OrganizationErrors.CannotTransferPersonalOrganization;
        }

        if (request.NewOwnerId == organization.OwnerId)
        {
            return OrganizationErrors.CannotTransferToSelf;
        }

        // The new owner must already be an active, unexpired member.
        var membership = await _organizationRepository.GetMembershipAsync(
            request.OrganizationId, request.NewOwnerId, cancellationToken);
        if (membership == null || !membership.IsValid())
        {
            return OrganizationErrors.CannotTransferOwnership;
        }

        var targetUser = await _userRepository.GetByIdAsync(request.NewOwnerId, cancellationToken);
        if (targetUser == null)
        {
            return OrganizationErrors.CannotTransferOwnership;
        }

        if (targetUser.Status != UserStatus.Active || !targetUser.EmailConfirmed || targetUser.IsSystemUser)
        {
            return OrganizationErrors.TransferTargetNotEligible;
        }

        // The sitting owner must always prove two-party consent with the code
        // emailed to the new owner. Only a platform admin acting on someone
        // else's organization (the recovery valve) skips it.
        if (isOwner)
        {
            var codeError = await VerifyTransferCodeAsync(request, cancellationToken);
            if (codeError is not null)
            {
                return codeError.Value;
            }
        }

        var ownerRole = await _roleRepository.GetByCodeAsync((Guid?)null, OrganizationRoleCodes.Owner, cancellationToken);
        if (ownerRole == null)
        {
            _logger.LogError("Organization owner role '{RoleCode}' not found in database", OrganizationRoleCodes.Owner);
            return Error.Unexpected(
                code: "Organization.OwnerRoleNotFound",
                description: "System configuration error: Organization owner role not found.");
        }

        var adminRole = await _roleRepository.GetByCodeAsync((Guid?)null, OrganizationRoleCodes.Admin, cancellationToken);
        if (adminRole == null)
        {
            _logger.LogError("Organization admin role '{RoleCode}' not found in database", OrganizationRoleCodes.Admin);
            return Error.Unexpected(
                code: "Organization.AdminRoleNotFound",
                description: "System configuration error: Organization admin role not found.");
        }

        // Single transaction: OwnerId + both membership roles. Conditional on
        // the owner we validated against, so a concurrent transfer loses cleanly.
        var previousOwnerId = organization.OwnerId;
        var transferred = await _organizationRepository.TransferOwnershipAsync(
            request.OrganizationId,
            previousOwnerId,
            request.NewOwnerId,
            ownerRole.Id,
            adminRole.Id,
            request.RequestedBy,
            cancellationToken);

        if (!transferred)
        {
            return OrganizationErrors.ConcurrentTransferConflict;
        }

        _logger.LogInformation(
            "Organization {OrganizationId} ownership transferred from {PreviousOwnerId} to {NewOwnerId} by {RequestedBy} (platformScope: {PlatformScope})",
            request.OrganizationId, previousOwnerId, request.NewOwnerId, request.RequestedBy, !isOwner);

        await _publisher.Publish(
            new OrganizationOwnershipTransferredEvent(
                organization.Id,
                organization.Name,
                previousOwnerId,
                request.NewOwnerId,
                request.RequestedBy,
                ViaPlatformScope: !isOwner),
            cancellationToken);

        return Result.Success;
    }

    /// <summary>
    /// Verifies the one-time code for the owner path. Returns null when the
    /// code is valid (and marks it used); otherwise the error to surface.
    /// </summary>
    private async Task<Error?> VerifyTransferCodeAsync(
        TransferOwnershipCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return OrganizationErrors.TransferCodeRequired;
        }

        var transferCode = await _transferCodeRepository.GetValidForOrganizationAsync(
            request.OrganizationId, cancellationToken);
        if (transferCode == null)
        {
            return OrganizationErrors.InvalidOrExpiredTransferCode;
        }

        // The code is bound to the target it was emailed to; re-targeting the
        // transfer invalidates the consent the code represents.
        if (transferCode.TargetUserId != request.NewOwnerId)
        {
            return OrganizationErrors.InvalidOrExpiredTransferCode;
        }

        if (transferCode.AttemptCount >= OwnershipTransferCode.MaxAttempts)
        {
            return OrganizationErrors.TransferCodeTooManyAttempts;
        }

        if (!_passwordHasher.VerifyPassword(request.Code, transferCode.CodeHash))
        {
            await _transferCodeRepository.IncrementAttemptCountAsync(transferCode.Id, cancellationToken);
            _logger.LogWarning(
                "Invalid ownership transfer code for organization {OrganizationId}. Attempt {Attempt} of {Max}",
                request.OrganizationId, transferCode.AttemptCount + 1, OwnershipTransferCode.MaxAttempts);
            return OrganizationErrors.InvalidOrExpiredTransferCode;
        }

        await _transferCodeRepository.MarkAsUsedAsync(transferCode.Id, cancellationToken);
        return null;
    }
}
