using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// What the address behind a matched sign-in code leads to (OI-215): a live account, an
/// account waiting for deletion, or none. The one classification the verify, sign-in
/// and recovery steps share.
/// </summary>
/// <remarks>
/// Called only after the code matched: the holder of the mailbox has proven themself,
/// so there is nothing left to hide from them. Before the code every address gets the
/// same answer, and nothing here may run then.
/// </remarks>
public class EmailCodeAccountResolver
{
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _deletionRequests;
    private readonly RegistrationSettings _registrationSettings;
    private readonly EmailSettings _emailSettings;

    public EmailCodeAccountResolver(
        IUserRepository userRepository,
        IAccountDeletionRequestRepository deletionRequests,
        IOptionsSnapshot<RegistrationSettings> registrationSettings,
        IOptionsSnapshot<EmailSettings> emailSettings)
    {
        _userRepository = userRepository;
        _deletionRequests = deletionRequests;
        _registrationSettings = registrationSettings.Value;
        _emailSettings = emailSettings.Value;
    }

    /// <summary>Resolves the account behind <paramref name="email"/>, the pending row's address.</summary>
    /// <returns>
    /// The account, or <see cref="UserErrors.SelfRegistrationClosed"/> for an address
    /// with no account while sign-up is closed, or <c>User.DuplicateEmail</c> for an
    /// account that is being deleted and can no longer be recovered.
    /// </returns>
    public async Task<ErrorOr<EmailCodeAccount>> ResolveAsync(string email, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is not null)
        {
            return new EmailCodeAccount(EmailCodeNextStep.SignIn, user, DeletionRequest: null);
        }

        // An account waiting for deletion is invisible to the lookup above; the code
        // proved its mailbox, so the way back is offered rather than a sign-up that
        // would collide with it.
        var deleted = await _userRepository.GetByEmailIncludeDeletedAsync(email, cancellationToken);
        if (deleted is { IsDeleted: true })
        {
            var request = await _deletionRequests.GetActiveByUserIdAsync(deleted.Id, cancellationToken);
            return request is { Status: AccountDeletionStatus.PendingGrace }
                ? new EmailCodeAccount(EmailCodeNextStep.Recover, deleted, request)
                : UserErrors.DuplicateEmail(email);
        }

        if (!_registrationSettings.AllowSelfRegistration)
        {
            return UserErrors.SelfRegistrationClosed;
        }

        var requirements = SignUpRequirements.From(_registrationSettings, _emailSettings);
        return new EmailCodeAccount(
            EmailCodeNextStep.CompleteAccount(requirements.PasswordRequired, requirements.NameRequired),
            User: null,
            DeletionRequest: null);
    }
}

/// <summary>What a matched code's address leads to.</summary>
/// <param name="Next">What the page does next.</param>
/// <param name="User">The account, live or waiting for deletion; null when there is none.</param>
/// <param name="DeletionRequest">The active deletion request of an account that waits for it.</param>
public sealed record EmailCodeAccount(EmailCodeNextStep Next, User? User, AccountDeletionRequest? DeletionRequest);
