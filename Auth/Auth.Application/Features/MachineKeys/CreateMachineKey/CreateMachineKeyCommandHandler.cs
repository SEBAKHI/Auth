using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Discovery.GetDiscoveryDocument;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.MachineKeys.CreateMachineKey;

/// <summary>
/// Handles <see cref="CreateMachineKeyCommand"/>.
/// </summary>
/// <remarks>
/// A generated private key exists in this process for one request: it is put in the answer and in
/// nothing else, no entity, no event, no log line. Okta's and Google's consoles hand out a
/// generated service key the same way; a team that wants the private key never to leave its own
/// machine pastes the public half instead.
/// </remarks>
public class CreateMachineKeyCommandHandler : IRequestHandler<CreateMachineKeyCommand, ErrorOr<CreateMachineKeyResponse>>
{
    private readonly IApplicationRepository _applications;
    private readonly IApplicationMachineKeyRepository _machineKeys;
    private readonly IMachineKeyMaterial _keyMaterial;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<CreateMachineKeyCommandHandler> _logger;

    public CreateMachineKeyCommandHandler(
        IApplicationRepository applications,
        IApplicationMachineKeyRepository machineKeys,
        IMachineKeyMaterial keyMaterial,
        IPublisher publisher,
        TimeProvider time,
        IOptionsSnapshot<JwtSettings> jwtSettings,
        ILogger<CreateMachineKeyCommandHandler> logger)
    {
        _applications = applications;
        _machineKeys = machineKeys;
        _keyMaterial = keyMaterial;
        _publisher = publisher;
        _time = time;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<CreateMachineKeyResponse>> Handle(
        CreateMachineKeyCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        MachinePublicKey publicKey;
        string? privateKeyPem = null;
        if (string.IsNullOrWhiteSpace(request.PublicKeyJwk))
        {
            var generated = _keyMaterial.Generate();
            publicKey = generated.PublicKey;
            privateKeyPem = generated.PrivateKeyPem;
        }
        else
        {
            var imported = _keyMaterial.ImportPublicJwk(request.PublicKeyJwk);
            if (imported.IsError)
            {
                return imported.Errors;
            }

            publicKey = imported.Value;
        }

        var now = _time.GetUtcNow();
        var created = ApplicationMachineKey.Create(
            application.Id, publicKey.Kid, publicKey.PublicKeyJwk, request.Name, request.CreatedBy, now.UtcDateTime,
            request.Purpose);
        if (created.IsError)
        {
            return created.Errors;
        }

        var key = created.Value;
        var outcome = await _machineKeys.TryCreateAsync(key, MachineClients.MaxActiveKeysPerApplication, cancellationToken);
        switch (outcome)
        {
            case MachineKeyCreateOutcome.LimitReached:
                return MachineKeyErrors.LimitReached;
            case MachineKeyCreateOutcome.DuplicateKey:
                // Only a pasted key can collide: one public key belongs to one application.
                return MachineKeyErrors.InvalidPublicKey;
        }

        _logger.LogInformation(
            "Machine key {Kid} ({Purpose}) created for application {ApplicationId} by {CreatedBy} ({Origin})",
            key.Kid, key.Purpose, application.Id, request.CreatedBy, privateKeyPem is null ? "pasted public key" : "generated");

        // After the commit, and not cancellable by the client: the key exists now.
        await _publisher.Publish(
            new MachineKeyCreatedEvent(
                Guid.NewGuid(), key.Id, application.Id, key.Kid, privateKeyPem is not null, request.CreatedBy, now,
                key.Purpose),
            CancellationToken.None);

        return new CreateMachineKeyResponse
        {
            Id = key.Id,
            Kid = key.Kid,
            ClientId = application.Code,
            Issuer = _jwtSettings.Issuer,
            TokenEndpoint = DiscoveryEndpoints.Token(request.PublicBaseUrl),
            Algorithm = key.Algorithm,
            Purpose = key.Purpose,
            PrivateKeyPem = privateKeyPem,
            CreatedAt = key.CreatedAt
        };
    }
}
