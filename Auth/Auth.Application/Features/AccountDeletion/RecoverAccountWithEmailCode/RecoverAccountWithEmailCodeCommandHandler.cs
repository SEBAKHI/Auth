using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountWithEmailCode;

/// <summary>
/// The emailed-code entry of the grace-period recovery (OI-215): the code proves the
/// mailbox as the provider's token proves the identity on the provider entry, and the
/// shared pipeline (<see cref="AccountDeletionRecoverer"/>) does the rest.
/// </summary>
/// <remarks>
/// <para>
/// The second step is the sign-in's: the authenticator app (or a recovery code) when
/// it is bound; nothing more when the second factor is the emailed code, which would
/// prove the same mailbox again. A password account that holds platform authority, or
/// whose page asked for full proof, continues with the password entry instead, as the
/// sign-in continues with the password sign-in.
/// </para>
/// <para>
/// Only an administrator's lock refuses, as on the provider entry: a lock raised by
/// wrong passwords is left in place, since a code proves the mailbox, never the
/// password (owner decision G8). The emailed code is spent only after the second
/// factor was accepted, inside the shared pipeline, so neither a missing nor a wrong
/// authenticator code costs the person a new email.
/// </para>
/// </remarks>
public class RecoverAccountWithEmailCodeCommandHandler
    : IRequestHandler<RecoverAccountWithEmailCodeCommand, ErrorOr<LoginResponse>>
{
    private readonly EmailCodeCheck _codeCheck;
    private readonly EmailCodeAccountResolver _accounts;
    private readonly IPendingRegistrationRepository _pendingRegistrations;
    private readonly EmailCodeFullProofRule _fullProof;
    private readonly ITwoFactorChallengeService _twoFactorChallengeService;
    private readonly AccountDeletionRecoverer _recoverer;

    public RecoverAccountWithEmailCodeCommandHandler(
        EmailCodeCheck codeCheck,
        EmailCodeAccountResolver accounts,
        IPendingRegistrationRepository pendingRegistrations,
        EmailCodeFullProofRule fullProof,
        ITwoFactorChallengeService twoFactorChallengeService,
        AccountDeletionRecoverer recoverer)
    {
        _codeCheck = codeCheck;
        _accounts = accounts;
        _pendingRegistrations = pendingRegistrations;
        _fullProof = fullProof;
        _twoFactorChallengeService = twoFactorChallengeService;
        _recoverer = recoverer;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        RecoverAccountWithEmailCodeCommand request, CancellationToken cancellationToken)
    {
        var checkedRow = await _codeCheck.CheckAsync(request.PendingId, request.Otp, cancellationToken);
        if (checkedRow.IsError)
        {
            return checkedRow.Errors;
        }

        var row = checkedRow.Value;

        var account = await _accounts.ResolveAsync(row.Email.Value, cancellationToken);
        if (account.IsError)
        {
            return account.Errors;
        }

        // A live account, or none: this is not the step for it; the answer says which
        // one is, and nothing was consumed.
        if (account.Value.Next.Step != EmailCodeSteps.Recover)
        {
            return new LoginResponse { NextStep = account.Value.Next };
        }

        var user = account.Value.User!;
        var deletionRequest = account.Value.DeletionRequest!;

        if (user.LockKind(DateTime.UtcNow) == AccountLockKind.Admin)
        {
            return user.LockRefusal(AccountLockKind.Admin);
        }

        if (await _fullProof.RequiresPasswordAsync(user, request.RequireFullProof, cancellationToken))
        {
            return new LoginResponse { NextStep = EmailCodeNextStep.PasswordRequiredToFinish };
        }

        // The authenticator app is asked before the code is spent: a person who has
        // not typed it yet keeps the code for the next try.
        if (user.TwoFactorEnabled
            && string.IsNullOrEmpty(request.TwoFactorCode)
            && !await _twoFactorChallengeService.SignInCodeProvesFactorAsync(user, cancellationToken))
        {
            return UserErrors.TwoFactorRequired;
        }

        // The code is spent by the pipeline, after the authenticator's code was
        // accepted and before the deletion is cancelled: a wrong app code leaves it
        // for the next try, and two requests with one code recover once.
        return await _recoverer.RecoverAsync(
            user,
            deletionRequest,
            AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: user.PasswordHash is not null),
            request.TwoFactorCode,
            request.IpAddress,
            request.UserAgent,
            request.DeviceId,
            cancellationToken,
            spendFirstProof: token => _pendingRegistrations.TryConsumeSignInCodeAsync(row.Id, request.Otp, token));
    }
}
