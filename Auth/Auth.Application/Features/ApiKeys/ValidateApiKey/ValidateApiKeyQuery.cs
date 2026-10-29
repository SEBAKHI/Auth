using Auth.Application.Features.MachineCredentialChecks;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.ValidateApiKey;

/// <summary>
/// An application's server asks whether an API key presented to it is one of its own, active
/// keys, and with what scopes and addresses (OI-128 PR B).
/// </summary>
/// <remarks>
/// No validator: the caller is answered first, and the key after it, so that an application that
/// may not ask learns that before anything about the key (the handler checks the key's shape).
/// </remarks>
public record ValidateApiKeyQuery(MachineCredentialCaller Caller, string RawApiKey) : IRequest<ErrorOr<ValidateApiKeyResponse>>;
