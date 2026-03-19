using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.SetCustomSecret;

/// <summary>
/// Command to set a custom secret value.
/// </summary>
public record SetCustomSecretCommand(
    string Key,
    string Value,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
