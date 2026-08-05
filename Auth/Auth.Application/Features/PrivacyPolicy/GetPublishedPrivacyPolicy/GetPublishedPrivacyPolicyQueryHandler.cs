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
    private readonly DataControllerSettings _controller;

    public GetPublishedPrivacyPolicyQueryHandler(
        IPrivacyPolicyVersionRepository repository,
        IOptionsSnapshot<AccountDeletionSettings> settings,
        IOptionsSnapshot<DataControllerSettings> controller)
    {
        _repository = repository;
        _settings = settings.Value;
        _controller = controller.Value;
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
            Disclosure = BuildDisclosure(_settings, _controller)
        };
    }

    /// <summary>Projects the configuration values the policy quotes.</summary>
    public static PrivacyPolicyDisclosureDto BuildDisclosure(
        AccountDeletionSettings settings,
        DataControllerSettings controller) => new()
    {
        LegalName = controller.LegalName,
        Address = controller.Address,
        PrivacyEmail = controller.PrivacyEmail,
        EmailProvider = controller.EmailProvider,
        HostingProvider = controller.HostingProvider,
        HostingCountry = controller.HostingCountry,
        DpoContact = controller.DpoContact,
        VerbisNo = controller.VerbisNo,
        KepAddress = controller.KepAddress,
        GraceDays = settings.GraceDays,
        OtpValidityMinutes = settings.OtpExpirationMinutes,
        LoginAttemptRetentionDays = settings.LoginAttemptRetentionDays,
        OutboxRetentionDays = settings.OutboxRetentionDays,
        // Effective, not raw: the sweep floors the reservation at the audit-log
        // retention, so the raw setting would publish a shorter window than the
        // one actually enforced.
        IdentifierReservationDays = settings.EffectiveIdentifierReservationDays,
        PolicyVersion = settings.PolicyVersion
    };
}
