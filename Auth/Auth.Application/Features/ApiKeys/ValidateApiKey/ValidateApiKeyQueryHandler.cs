using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Services;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.ValidateApiKey;

/// <summary>
/// Validates an API key for the application that asks (OI-128 PR B): shape and check, then the
/// one row its selector names, then the binding to the caller, then one Argon2id verify.
/// </summary>
/// <remarks>
/// <para>
/// A key is reported only to its own application. An unknown key, a malformed one, another
/// application's key, and a revoked or expired one all get the same answer,
/// <see cref="ApiKeyErrors.Invalid"/>. Another application's key is refused before the hash and
/// records no use, so one application can neither spend this server's hashing on another's keys
/// nor touch their last-used time.
/// </para>
/// <para>
/// A key minted before the selector format has no selector and no longer validates: rotate it.
/// </para>
/// </remarks>
public class ValidateApiKeyQueryHandler : IRequestHandler<ValidateApiKeyQuery, ErrorOr<ValidateApiKeyResponse>>
{
    private const string Scheme = "ak_";

    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ValidateApiKeyQueryHandler> _logger;

    public ValidateApiKeyQueryHandler(
        IApiKeyRepository apiKeyRepository,
        IApplicationRepository applicationRepository,
        IPasswordHasher passwordHasher,
        ILogger<ValidateApiKeyQueryHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _applicationRepository = applicationRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ErrorOr<ValidateApiKeyResponse>> Handle(
        ValidateApiKeyQuery request,
        CancellationToken cancellationToken)
    {
        var caller = await request.Caller.ResolveAsync(
            _applicationRepository, MachineClients.Scopes.ApiKeysValidate, cancellationToken);
        if (caller.IsError)
        {
            return caller.Errors;
        }

        var application = caller.Value;
        var rawApiKey = request.RawApiKey;

        if (string.IsNullOrEmpty(rawApiKey))
        {
            return ApiKeyErrors.Required;
        }

        if (!rawApiKey.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return ApiKeyErrors.InvalidPrefix;
        }

        // A malformed key, or one whose check does not match, never reaches the database.
        if (!ApiKeyFormat.TryParse(rawApiKey, out _, out var selector))
        {
            _logger.LogWarning("API key validation failed for application {ApplicationId}: not a well-formed key", application.Id);
            return ApiKeyErrors.Invalid;
        }

        var apiKey = await _apiKeyRepository.GetActiveBySelectorAsync(selector, cancellationToken);
        if (apiKey is null)
        {
            _logger.LogWarning("API key validation failed for application {ApplicationId}: no active key", application.Id);
            return ApiKeyErrors.Invalid;
        }

        // Before the hash, and before any write: another application's key is an unknown key here.
        if (apiKey.ApplicationId != application.Id)
        {
            _logger.LogWarning(
                "API key validation refused: application {ApplicationId} presented key {ApiKeyId} of application {KeyApplicationId}",
                application.Id, apiKey.Id, apiKey.ApplicationId);
            return ApiKeyErrors.Invalid;
        }

        if (!_passwordHasher.VerifyPassword(rawApiKey, apiKey.KeyHash))
        {
            _logger.LogWarning("API key validation failed for application {ApplicationId}: key {ApiKeyId} did not verify", application.Id, apiKey.Id);
            return ApiKeyErrors.Invalid;
        }

        await _apiKeyRepository.RecordUsageAsync(apiKey.Id, cancellationToken);
        var scopes = await _apiKeyRepository.GetScopesAsync(apiKey.Id, cancellationToken);

        _logger.LogInformation(
            "API key validated: {ApiKeyId} for application {ApplicationId}",
            apiKey.Id, apiKey.ApplicationId);

        return new ValidateApiKeyResponse
        {
            Active = true,
            ApiKeyId = apiKey.Id,
            ApplicationId = apiKey.ApplicationId,
            ApplicationCode = application.Code,
            Name = apiKey.Name,
            Environment = apiKey.Environment,
            Scopes = scopes,
            AllowedIps = apiKey.AllowedIps.Entries
        };
    }
}
