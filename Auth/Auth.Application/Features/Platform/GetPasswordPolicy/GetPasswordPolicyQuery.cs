using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.GetPasswordPolicy;

/// <summary>
/// Query for the public password policy: the composition rules a sign-up,
/// invitation, reset or change-password form shows and checks while a person
/// types. Served anonymously, because every one of those forms except the
/// last is reached before a session exists.
/// </summary>
public record GetPasswordPolicyQuery() : IRequest<ErrorOr<PasswordPolicyDto>>;
