using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyRegistration;

/// <summary>
/// The second step of a verify-first self-registration: checks the code against
/// the pending row and consumes nothing. The same code is presented again at
/// completion, which is the step that consumes it.
/// </summary>
/// <param name="PendingId">The handle the start step answered with.</param>
/// <param name="Otp">The six-digit code that reached the address.</param>
public record VerifyRegistrationCommand(string PendingId, string Otp) : IRequest<ErrorOr<Success>>;
