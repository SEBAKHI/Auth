using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyEmailCodeSignIn;

/// <summary>
/// Checks the code of a sign-in by emailed code and consumes nothing (OI-215): after a
/// match, says what happens next, which only the mailbox's holder may learn.
/// </summary>
/// <param name="PendingId">The handle the start step answered with.</param>
/// <param name="Otp">The six-digit code that reached the address.</param>
public record VerifyEmailCodeSignInCommand(string PendingId, string Otp) : IRequest<ErrorOr<EmailCodeNextStep>>;
