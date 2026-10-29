using Auth.Application.Features.LoginDesigns.Common;
using FluentValidation;

namespace Auth.Application.Features.LoginDesigns.UpdateLoginDesign;

/// <summary>Validates <see cref="UpdateLoginDesignCommand"/>.</summary>
public sealed class UpdateLoginDesignCommandValidator : AbstractValidator<UpdateLoginDesignCommand>
{
    public UpdateLoginDesignCommandValidator()
    {
        RuleFor(x => x.Name).ValidDesignName();
        RuleFor(x => x.Content).ValidDesignContent();
        RuleFor(x => x.StringsJson).ValidDesignStrings();
    }
}
