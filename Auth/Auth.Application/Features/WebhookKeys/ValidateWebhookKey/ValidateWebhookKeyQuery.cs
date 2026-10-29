using Auth.Application.Features.MachineCredentialChecks;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.ValidateWebhookKey;

/// <summary>
/// An application's server asks whether a webhook key presented to it is one of its own, active
/// keys (OI-128 PR B).
/// </summary>
/// <remarks>
/// No validator: the caller is answered first, and the key after it (the handler checks the
/// key's shape).
/// </remarks>
public record ValidateWebhookKeyQuery(MachineCredentialCaller Caller, string RawWebhookKey) : IRequest<ErrorOr<ValidateWebhookKeyResponse>>;
