using Auth.Application.Features.LoginDesigns.Common;
using FluentValidation;

namespace Auth.Application.Features.LoginDesigns.CopyLoginDesign;

/// <summary>Validates <see cref="CopyLoginDesignCommand"/>.</summary>
public sealed class CopyLoginDesignCommandValidator : AbstractValidator<CopyLoginDesignCommand>
{
    public CopyLoginDesignCommandValidator()
    {
        RuleFor(x => x.Name).ValidDesignName();
    }
}
