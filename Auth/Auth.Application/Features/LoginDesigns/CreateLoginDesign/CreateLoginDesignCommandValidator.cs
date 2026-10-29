using Auth.Application.Features.LoginDesigns.Common;
using FluentValidation;

namespace Auth.Application.Features.LoginDesigns.CreateLoginDesign;

/// <summary>Validates <see cref="CreateLoginDesignCommand"/>; absent content starts from the starter.</summary>
public sealed class CreateLoginDesignCommandValidator : AbstractValidator<CreateLoginDesignCommand>
{
    public CreateLoginDesignCommandValidator()
    {
        RuleFor(x => x.Name).ValidDesignName();
        RuleFor(x => x.Content).ValidDesignContent().When(x => x.Content is not null);
        RuleFor(x => x.StringsJson).ValidDesignStrings();
    }
}
