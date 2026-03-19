using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GetSecretStatus;

/// <summary>
/// Query to retrieve the status of all configured secrets.
/// </summary>
public record GetSecretStatusQuery : IRequest<ErrorOr<SecretStatusResult>>;
