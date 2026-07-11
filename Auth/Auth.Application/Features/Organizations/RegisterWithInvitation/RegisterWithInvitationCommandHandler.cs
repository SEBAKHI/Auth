using Auth.Application.DTOs;
using Auth.Application.Features.Organizations.AcceptInvitation;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.RegisterWithInvitation;

/// <summary>
/// Handler for registering a new account through an organization invitation.
/// Creates the user with the invited email marked as confirmed (token possession
/// proves mailbox ownership, same precedent as external-provider sign-up) and
/// accepts the invitation in the same step. No verification OTP is sent.
/// </summary>
public class RegisterWithInvitationCommandHandler
    : IRequestHandler<RegisterWithInvitationCommand, ErrorOr<RegisterWithInvitationResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly IPasswordBreachEvaluator _breachEvaluator;
    private readonly IMediator _mediator;
    private readonly ILogger<RegisterWithInvitationCommandHandler> _logger;

    public RegisterWithInvitationCommandHandler(
        IUserRepository userRepository,
        IOrganizationRepository organizationRepository,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IMediator mediator,
        ILogger<RegisterWithInvitationCommandHandler> logger)
    {
        _userRepository = userRepository;
        _organizationRepository = organizationRepository;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<ErrorOr<RegisterWithInvitationResponse>> Handle(
        RegisterWithInvitationCommand request,
        CancellationToken cancellationToken)
    {
        // All pre-checks run before creating the user so a failure cannot
        // leave a half-onboarded account behind.
        var invitation = await _organizationRepository.GetInvitationByTokenAsync(request.Token, cancellationToken);
        if (invitation == null)
        {
            return OrganizationErrors.InvitationNotFoundByToken;
        }

        if (invitation.Status == InvitationStatus.Accepted)
        {
            return OrganizationErrors.InvitationAlreadyAccepted;
        }

        if (invitation.Status == InvitationStatus.Declined)
        {
            return OrganizationErrors.InvitationAlreadyDeclined;
        }

        if (invitation.Status == InvitationStatus.Cancelled)
        {
            return OrganizationErrors.InvitationAlreadyCancelled;
        }

        if (invitation.IsExpired())
        {
            invitation.MarkExpired();
            await _organizationRepository.UpdateInvitationAsync(invitation, cancellationToken);
            return OrganizationErrors.InvitationExpired;
        }

        var organization = await _organizationRepository.GetByIdAsync(invitation.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(invitation.OrganizationId);
        }

        if (!organization.IsActive)
        {
            return OrganizationErrors.Inactive(invitation.OrganizationId);
        }

        // An existing account must go through the sign-in + accept path instead
        if (await _userRepository.ExistsByEmailAsync(invitation.Email.Value, cancellationToken))
        {
            return UserErrors.DuplicateEmail(invitation.Email.Value);
        }

        var passwordValidation = _passwordValidator.Validate(request.Password);
        if (passwordValidation.IsError)
        {
            return passwordValidation.Errors;
        }

        var breachResult = await _breachEvaluator.EvaluateAsync(request.Password, cancellationToken);
        if (breachResult.IsError)
        {
            return breachResult.Errors;
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var user = User.Create(
            email: invitation.Email.Value,
            passwordHash: passwordHash,
            firstName: request.FirstName,
            lastName: request.LastName,
            createdBy: Guid.Empty,
            preferredLanguage: request.PreferredLanguage ?? "en",
            timeZone: request.TimeZone ?? "UTC");

        // The invitation token was delivered to this mailbox; possession proves ownership.
        user.ConfirmEmail(user.Id);

        await _userRepository.CreateAsync(user, cancellationToken);

        var acceptResult = await _mediator.Send(
            new AcceptInvitationCommand(request.Token) { AcceptedBy = user.Id },
            cancellationToken);

        if (acceptResult.IsError)
        {
            // The confirmed account exists and can sign in and accept manually.
            _logger.LogWarning(
                "User {UserId} registered via invitation {InvitationId} but acceptance failed: {Error}",
                user.Id, invitation.Id, acceptResult.FirstError.Description);
            return acceptResult.Errors;
        }

        _logger.LogInformation(
            "User {UserId} registered via invitation and joined organization {OrganizationId}",
            user.Id, invitation.OrganizationId);

        return new RegisterWithInvitationResponse(
            user.Id,
            user.Email.Value,
            acceptResult.Value.OrganizationName,
            acceptResult.Value.RoleName,
            "Account created and invitation accepted. Please sign in.");
    }
}
