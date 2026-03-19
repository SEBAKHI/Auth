using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.DeleteCustomSecret;

/// <summary>
/// Command to delete a custom secret.
/// </summary>
public record DeleteCustomSecretCommand(
    string Key,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
