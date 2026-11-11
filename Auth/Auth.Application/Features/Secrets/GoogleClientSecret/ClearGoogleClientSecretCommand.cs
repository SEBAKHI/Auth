using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GoogleClientSecret;

/// <summary>
/// Removes Google's OAuth client secret from the encrypted secrets file. After the next
/// API restart the code exchange answers <c>ExternalAuth.ProviderNotConfigured</c>, and the
/// apps' Google button fails with that sentence until a secret is set again.
/// </summary>
/// <param name="RequestedBy">The administrator performing the change.</param>
public record ClearGoogleClientSecretCommand(Guid RequestedBy) : IRequest<ErrorOr<Success>>;
