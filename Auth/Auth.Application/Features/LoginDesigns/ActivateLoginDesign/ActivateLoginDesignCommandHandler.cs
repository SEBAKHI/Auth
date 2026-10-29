using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.ActivateLoginDesign;

/// <summary>
/// Handles <see cref="ActivateLoginDesignCommand"/>. Activating the active design changes
/// nothing and announces nothing. Two activations racing in one scope cannot both commit: the
/// database refuses the second (a unique violation), which answers 409.
/// </summary>
public sealed class ActivateLoginDesignCommandHandler
    : IRequestHandler<ActivateLoginDesignCommand, ErrorOr<LoginDesignDto>>
{
    private readonly ILoginDesignRepository _designs;
    private readonly LoginDesignScope _scope;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<ActivateLoginDesignCommandHandler> _logger;

    public ActivateLoginDesignCommandHandler(
        ILoginDesignRepository designs,
        LoginDesignScope scope,
        IPublisher publisher,
        TimeProvider time,
        ILogger<ActivateLoginDesignCommandHandler> logger)
    {
        _designs = designs;
        _scope = scope;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginDesignDto>> Handle(
        ActivateLoginDesignCommand request,
        CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.ApplicationId, request.DesignId, cancellationToken);
        if (loaded.IsError)
        {
            return loaded.Errors;
        }

        var design = loaded.Value;
        if (!design.Activate())
        {
            return LoginDesignDto.From(design);
        }

        var activation = await _designs.ActivateAsync(design, cancellationToken);
        if (!activation.Activated)
        {
            _logger.LogWarning(
                "Activation of sign-in page design {DesignId} lost a race in its scope",
                design.Id);
            return LoginDesignErrors.ActivationConflict;
        }

        _logger.LogInformation(
            "Sign-in page design {DesignId} activated by {ActivatedBy}, replacing {PreviousId}",
            design.Id, request.ActivatedBy, activation.PreviousActiveId);

        await _publisher.Publish(
            new LoginDesignActivatedEvent(
                Guid.NewGuid(),
                design.Id,
                design.ApplicationId,
                LoginDesignSnapshot.Of(design),
                activation.PreviousActiveId,
                request.ActivatedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return LoginDesignDto.From(design);
    }
}
