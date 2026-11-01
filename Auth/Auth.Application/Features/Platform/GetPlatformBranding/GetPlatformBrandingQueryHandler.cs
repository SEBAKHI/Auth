using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Platform.GetPlatformBranding;

/// <summary>
/// Handler for the public platform branding query. Falls back to the default
/// branding when the settings row has not been seeded yet. Also carries the public
/// sign-in options the sign-in page acts on (OI-215), read from the effective
/// settings: what they say, and nothing about any account.
/// </summary>
public class GetPlatformBrandingQueryHandler : IRequestHandler<GetPlatformBrandingQuery, ErrorOr<PlatformBrandingDto>>
{
    private readonly IPlatformSettingsRepository _platformSettingsRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly RegistrationSettings _registrationSettings;
    private readonly EmailSettings _emailSettings;
    private readonly EffectiveLoginDesign _loginDesign;

    public GetPlatformBrandingQueryHandler(
        IPlatformSettingsRepository platformSettingsRepository,
        IImageUrlComposer imageUrlComposer,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        IOptionsSnapshot<RegistrationSettings> registrationSettings,
        IOptionsSnapshot<EmailSettings> emailSettings,
        EffectiveLoginDesign loginDesign)
    {
        _loginDesign = loginDesign;
        _platformSettingsRepository = platformSettingsRepository;
        _imageUrlComposer = imageUrlComposer;
        _idpSettings = idpSettings.Value;
        _registrationSettings = registrationSettings.Value;
        _emailSettings = emailSettings.Value;
    }

    public async Task<ErrorOr<PlatformBrandingDto>> Handle(GetPlatformBrandingQuery request, CancellationToken cancellationToken)
    {
        var settings = await _platformSettingsRepository.GetAsync(cancellationToken)
            ?? PlatformSettings.CreateDefault();

        // The effective switches: the password one is honoured only while email is on
        // (SignUpRequirements), and the code is primary exactly when a new account
        // signs up without a password (D-215-6 (a)).
        var signUp = SignUpRequirements.From(_registrationSettings, _emailSettings);

        return new PlatformBrandingDto
        {
            PlatformName = settings.PlatformName,
            LogoUrl = _imageUrlComposer.Compose(settings.LogoUrl),
            LogoUrlDark = _imageUrlComposer.Compose(settings.LogoUrlDark),
            FaviconUrl = _imageUrlComposer.Compose(settings.FaviconUrl),
            LogoHeightPx = settings.LogoHeight?.Px,
            Theme = PlatformThemeDto.From(settings.Theme),
            Fonts = PlatformFontsDto.From(settings, _imageUrlComposer),
            StaySignedInOffered = _idpSettings.StaySignedInOffered,
            CodeSignInOffered = _emailSettings.Enabled,
            PrimaryMethod = signUp.PasswordRequired ? SignInMethods.Password : SignInMethods.Code,
            SelfRegistrationOpen = _registrationSettings.AllowSelfRegistration,
            PasswordAtSignUp = signUp.PasswordRequired,
            NameAtSignUp = signUp.NameRequired,
            LoginDesign = LoginDesignRefDto.FromOrNull(await _loginDesign.ForAsync(null, cancellationToken))
        };
    }
}
