using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Organizations.InitiateOwnershipTransfer;

/// <summary>
/// Handler for initiating an organization ownership transfer. Generates a
/// one-time code, stores its Argon2id hash, and emails the code to the
/// prospective new owner — their handing it back to the current owner is the
/// proof that both parties consent to the transfer.
/// </summary>
public class InitiateOwnershipTransferCommandHandler
    : IRequestHandler<InitiateOwnershipTransferCommand, ErrorOr<InitiateOwnershipTransferResponse>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IOwnershipTransferCodeRepository _transferCodeRepository;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly INotificationService _notificationService;
    private readonly IPublisher _publisher;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<InitiateOwnershipTransferCommandHandler> _logger;

    public InitiateOwnershipTransferCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IOwnershipTransferCodeRepository transferCodeRepository,
        IOtpGenerator otpGenerator,
        IPasswordHasher passwordHasher,
        INotificationService notificationService,
        IPublisher publisher,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<InitiateOwnershipTransferCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _transferCodeRepository = transferCodeRepository;
        _otpGenerator = otpGenerator;
        _passwordHasher = passwordHasher;
        _notificationService = notificationService;
        _publisher = publisher;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<InitiateOwnershipTransferResponse>> Handle(
        InitiateOwnershipTransferCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Initiation is strictly owner-only: the code flow exists to prove the
        // sitting owner consents. Platform admins use the direct transfer path.
        if (organization.OwnerId != request.RequestedBy)
        {
            return OrganizationErrors.NotOwner;
        }

        // Personal organizations are bound to their account and never change hands.
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

        // Ownership must land on a live, reachable account: the code email goes
        // to this address, so an unconfirmed one cannot prove consent.
        if (targetUser.Status != UserStatus.Active || !targetUser.EmailConfirmed || targetUser.IsSystemUser)
        {
            return OrganizationErrors.TransferTargetNotEligible;
        }

        // Rate limiting per organization
        var recentCount = await _transferCodeRepository.GetRecentCountForOrganizationAsync(
            request.OrganizationId, _emailSettings.RateLimitWindow, cancellationToken);
        if (recentCount >= _emailSettings.MaxOtpRequestsPerWindow)
        {
            _logger.LogWarning(
                "Rate limit exceeded for ownership transfer codes on organization {OrganizationId}",
                request.OrganizationId);
            return OrganizationErrors.TooManyTransferRequests;
        }

        // A new code supersedes any outstanding one
        await _transferCodeRepository.InvalidateAllForOrganizationAsync(request.OrganizationId, cancellationToken);

        var otp = _otpGenerator.GenerateNumericOtp(6);
        var otpHash = _passwordHasher.HashPassword(otp);

        // Log the code when email is disabled (development mode); the email is
        // the only other place it exists.
        if (!_emailSettings.Enabled)
        {
            _logger.LogWarning(
                "Email disabled - Ownership transfer code for organization {OrganizationId} (target {Email}): {Otp} (expires in {Minutes} minutes)",
                request.OrganizationId, EmailMasking.Mask(targetUser.Email), otp, _emailSettings.OtpExpirationMinutes);
        }

        var transferCode = OwnershipTransferCode.Create(
            request.OrganizationId,
            request.NewOwnerId,
            request.RequestedBy,
            otpHash,
            _emailSettings.OtpExpirationMinutes);

        await _transferCodeRepository.CreateAsync(transferCode, cancellationToken);

        var owner = await _userRepository.GetByIdAsync(organization.OwnerId, cancellationToken);
        var ownerName = owner?.DisplayName ?? owner?.FirstName ?? "The organization owner";
        var targetName = targetUser.DisplayName ?? targetUser.FirstName ?? "User";

        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.OwnershipTransferCode,
                RecipientAddress = targetUser.Email,
                RecipientName = targetName,
                RecipientUserId = targetUser.Id,
                Variables = new Dictionary<string, object?>
                {
                    ["TargetName"] = targetName,
                    ["OwnerName"] = ownerName,
                    ["OrganizationName"] = organization.Name,
                    ["OtpCode"] = otp,
                    ["ExpirationMinutes"] = _emailSettings.OtpExpirationMinutes
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send ownership transfer code for organization {OrganizationId} to user {UserId}: {Error}",
                request.OrganizationId, targetUser.Id, sendResult.FirstError.Description);
            return OrganizationErrors.TransferCodeEmailFailed;
        }

        _logger.LogInformation(
            "Ownership transfer initiated for organization {OrganizationId} by {RequestedBy}; code sent to user {TargetUserId}",
            request.OrganizationId, request.RequestedBy, request.NewOwnerId);

        await _publisher.Publish(
            new OrganizationOwnershipTransferInitiatedEvent(
                organization.Id,
                organization.Name,
                request.NewOwnerId,
                request.RequestedBy),
            cancellationToken);

        return new InitiateOwnershipTransferResponse(
            transferCode.ExpiresAt,
            EmailMasking.Mask(targetUser.Email));
    }
}
