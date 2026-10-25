using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.GetEmailCodeFactorCount;

/// <summary>
/// How many accounts have the emailed code as their second factor (S24, ARR-0694):
/// the number the console shows before email sending is switched off, because each of
/// those accounts can then sign in with a recovery code only.
/// </summary>
public record GetEmailCodeFactorCountQuery : IRequest<ErrorOr<EmailCodeFactorCountResponse>>;

/// <summary>
/// The count, and nothing else: no account is named.
/// </summary>
/// <param name="Count">Accounts whose second factor is the emailed code.</param>
public record EmailCodeFactorCountResponse(int Count);
