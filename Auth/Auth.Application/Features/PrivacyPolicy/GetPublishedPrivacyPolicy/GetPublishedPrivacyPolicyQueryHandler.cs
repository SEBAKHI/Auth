using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;

/// <summary>
/// Handler serving the published policy document plus the live disclosure
/// values. The numbers come from the running configuration on every request,
/// so a change to appsettings is reflected in the published policy without a
/// content edit or a deployment.
/// </summary>
public class GetPublishedPrivacyPolicyQueryHandler
    : IRequestHandler<GetPublishedPrivacyPolicyQuery, ErrorOr<PublishedPrivacyPolicyDto>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;
    private readonly AccountDeletionSettings _settings;

    public GetPublishedPrivacyPolicyQueryHandler(
        IPrivacyPolicyVersionRepository repository,
        IOptionsSnapshot<AccountDeletionSettings> settings)
    {
        _repository = repository;
        _settings = settings.Value;
    }

    public async Task<ErrorOr<PublishedPrivacyPolicyDto>> Handle(
        GetPublishedPrivacyPolicyQuery request, CancellationToken cancellationToken)
    {
        var version = await _repository.GetPublishedAsync(cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NoPublishedVersion;
        }

        var requested = PolicyLanguages.Normalize(request.LanguageCode)
            ?? PolicyLanguages.Fallback;

        var translation = await _repository.GetTranslationAsync(
            version.Id, requested, cancellationToken);

        // An unwritten language falls back to the neutral document rather than
        // showing nothing — a policy must always be readable.
        translation ??= await _repository.GetTranslationAsync(
            version.Id, PolicyLanguages.Fallback, cancellationToken);

        if (translation is null)
        {
            return PrivacyPolicyErrors.NoPublishedVersion;
        }

        return new PublishedPrivacyPolicyDto
        {
            Version = version.Version,
            EffectiveDateUtc = version.EffectiveDateUtc,
            LanguageCode = translation.LanguageCode,
            ContentJson = translation.ContentJson,
            Disclosure = BuildDisclosure(_settings)
        };
    }

    /// <summary>Projects the configuration values the policy quotes.</summary>
    public static PrivacyPolicyDisclosureDto BuildDisclosure(AccountDeletionSettings settings) => new()
    {
        GraceDays = settings.GraceDays,
        OtpValidityMinutes = settings.OtpExpirationMinutes,
        LoginAttemptRetentionDays = settings.LoginAttemptRetentionDays,
        OutboxRetentionDays = settings.OutboxRetentionDays,
        PolicyVersion = settings.PolicyVersion
    };
}
