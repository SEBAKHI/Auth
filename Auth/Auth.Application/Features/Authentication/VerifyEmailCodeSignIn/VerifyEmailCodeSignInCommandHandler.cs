using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyEmailCodeSignIn;

/// <summary>
/// Checks a sign-in code without consuming it, and after a match says what happens
/// next (OI-215): sign in, recover an account waiting for deletion, or complete a new
/// account with the fields the settings ask for.
/// </summary>
/// <remarks>
/// <para>
/// Before the match there is no branch on anything but the code: a wrong code, an
/// unknown handle and a dead code get the answers the registration's check gives, so
/// a guesser learns nothing about the address. After it, the holder of the mailbox
/// has proven themself and is told the truth.
/// </para>
/// <para>
/// The attempt is charged under the row's lock (<see cref="EmailCodeCheck"/>), on the
/// counter every step shares: five wrong codes close the code whichever step they came
/// through. The sign-in and recovery steps check the same code again, and consume it.
/// </para>
/// </remarks>
public class VerifyEmailCodeSignInCommandHandler
    : IRequestHandler<VerifyEmailCodeSignInCommand, ErrorOr<EmailCodeNextStep>>
{
    private readonly EmailCodeCheck _codeCheck;
    private readonly EmailCodeAccountResolver _accounts;

    public VerifyEmailCodeSignInCommandHandler(EmailCodeCheck codeCheck, EmailCodeAccountResolver accounts)
    {
        _codeCheck = codeCheck;
        _accounts = accounts;
    }

    public async Task<ErrorOr<EmailCodeNextStep>> Handle(
        VerifyEmailCodeSignInCommand request,
        CancellationToken cancellationToken)
    {
        var row = await _codeCheck.CheckAsync(request.PendingId, request.Otp, cancellationToken);
        if (row.IsError)
        {
            return row.Errors;
        }

        var account = await _accounts.ResolveAsync(row.Value.Email.Value, cancellationToken);
        if (account.IsError)
        {
            return account.Errors;
        }

        return account.Value.Next;
    }
}
