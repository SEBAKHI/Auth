using Auth.Application.Features.LoginDesigns.Common;
using FluentValidation;

namespace Auth.Application.Features.LoginDesigns.PreviewLoginDesign;

/// <summary>Validates <see cref="PreviewLoginDesignQuery"/>: the same limits as a save.</summary>
public sealed class PreviewLoginDesignQueryValidator : AbstractValidator<PreviewLoginDesignQuery>
{
    public PreviewLoginDesignQueryValidator()
    {
        RuleFor(x => x.Content).ValidDesignContent();
        RuleFor(x => x.StringsJson).ValidDesignStrings();
        RuleFor(x => x.Language).SupportedDesignLanguage();
    }
}
