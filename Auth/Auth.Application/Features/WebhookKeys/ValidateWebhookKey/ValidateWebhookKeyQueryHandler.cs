using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Auth.Domain.Enums;
using Auth.Domain.Events;

namespace Auth.Application.Features.WebhookKeys.ValidateWebhookKey;

/// <summary>
/// Handler for validating a webhook key using deterministic HMAC-SHA256 hash lookup, for the
/// application that asks (OI-128 PR B).
/// </summary>
/// <remarks>
/// <para>
/// A key is reported only to its own application. Another application's key gets the answer of
/// an unknown key, <see cref="WebhookKeyErrors.Invalid"/>, and touches nothing: no usage stamp,
/// no re-fingerprint.
/// </para>
/// <para>
/// A key whose stored fingerprint was computed under an HMAC key that no longer issues
/// (S11) still validates through that key's 90-day grace, and its first successful use
/// asks for it to be re-fingerprinted under the issuing key: the event's handler
/// rewrites the row, conditionally, and nothing it does can fail this validation.
/// </para>
/// </remarks>
public class ValidateWebhookKeyQueryHandler : IRequestHandler<ValidateWebhookKeyQuery, ErrorOr<ValidateWebhookKeyResponse>>
{
    private const string Scheme = "wk_";

    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IPublisher _publisher;
    private readonly ILogger<ValidateWebhookKeyQueryHandler> _logger;

    public ValidateWebhookKeyQueryHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IApplicationRepository applicationRepository,
        ICredentialHasher credentialHasher,
        IPublisher publisher,
        ILogger<ValidateWebhookKeyQueryHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _applicationRepository = applicationRepository;
        _credentialHasher = credentialHasher;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<ValidateWebhookKeyResponse>> Handle(
        ValidateWebhookKeyQuery request,
        CancellationToken cancellationToken)
    {
        var caller = await request.Caller.ResolveAsync(
            _applicationRepository, MachineClients.Scopes.WebhookKeysValidate, cancellationToken);
        if (caller.IsError)
        {
            return caller.Errors;
        }

        var application = caller.Value;
        var rawWebhookKey = request.RawWebhookKey;

        if (string.IsNullOrEmpty(rawWebhookKey))
        {
            return WebhookKeyErrors.Required;
        }

        if (!rawWebhookKey.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return WebhookKeyErrors.InvalidPrefix;
        }

        var candidates = _credentialHasher.Candidates(CredentialPurpose.WebhookKey, rawWebhookKey);

        var webhookKey = await _webhookKeyRepository.GetByHashAsync(candidates, cancellationToken);
        if (webhookKey is null)
        {
            _logger.LogWarning("Webhook key validation failed for application {ApplicationId}: key not found", application.Id);
            return WebhookKeyErrors.Invalid;
        }

        // Before any write: another application's key is an unknown key here.
        if (webhookKey.ApplicationId != application.Id)
        {
            _logger.LogWarning(
                "Webhook key validation refused: application {ApplicationId} presented key {WebhookKeyId} of application {KeyApplicationId}",
                application.Id, webhookKey.Id, webhookKey.ApplicationId);
            return WebhookKeyErrors.Invalid;
        }

        await _webhookKeyRepository.RecordUsageAsync(webhookKey.Id, cancellationToken);

        var issuing = _credentialHasher.Hash(CredentialPurpose.WebhookKey, rawWebhookKey);
        if (!string.Equals(webhookKey.KeyHash, issuing, StringComparison.Ordinal))
        {
            // The validation has succeeded; the re-fingerprint must not undo that, so it
            // is the event's handler's, which never throws, and it is not cancelled with
            // the caller's request.
            await _publisher.Publish(
                new WebhookKeyHashUpgradedEvent(webhookKey.Id, webhookKey.KeyHash, issuing),
                CancellationToken.None);
        }

        _logger.LogInformation(
            "Webhook key validated: {WebhookKeyId} for application {ApplicationId}",
            webhookKey.Id, webhookKey.ApplicationId);

        return new ValidateWebhookKeyResponse
        {
            Active = true,
            WebhookKeyId = webhookKey.Id,
            ApplicationId = webhookKey.ApplicationId,
            ApplicationCode = application.Code,
            Name = webhookKey.Name,
            TargetUrl = webhookKey.TargetUrl,
            Environment = webhookKey.Environment
        };
    }
}
