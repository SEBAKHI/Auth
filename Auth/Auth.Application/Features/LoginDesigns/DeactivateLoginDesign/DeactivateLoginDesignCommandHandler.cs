using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.DeactivateLoginDesign;

/// <summary>
/// Handles <see cref="DeactivateLoginDesignCommand"/>. Deactivating an inactive design, or one
/// another activation has just replaced, changes nothing and announces nothing.
/// </summary>
public sealed class DeactivateLoginDesignCommandHandler
    : IRequestHandler<DeactivateLoginDesignCommand, ErrorOr<LoginDesignDto>>
{
    private readonly ILoginDesignRepository _designs;
    private readonly LoginDesignScope _scope;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<DeactivateLoginDesignCommandHandler> _logger;

    public DeactivateLoginDesignCommandHandler(
        ILoginDesignRepository designs,
        LoginDesignScope scope,
        IPublisher publisher,
        TimeProvider time,
        ILogger<DeactivateLoginDesignCommandHandler> logger)
    {
        _designs = designs;
        _scope = scope;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginDesignDto>> Handle(
        DeactivateLoginDesignCommand request,
        CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.ApplicationId, request.DesignId, cancellationToken);
        if (loaded.IsError)
        {
            return loaded.Errors;
        }

        var design = loaded.Value;
        if (!design.Deactivate())
        {
            return LoginDesignDto.From(design);
        }

        if (!await _designs.DeactivateAsync(design, cancellationToken))
        {
            // Replaced or deleted between the read and this write: it is not active either way.
            return LoginDesignDto.From(design);
        }

        _logger.LogInformation(
            "Sign-in page design {DesignId} deactivated by {DeactivatedBy}",
            design.Id, request.DeactivatedBy);

        await _publisher.Publish(
            new LoginDesignDeactivatedEvent(
                Guid.NewGuid(),
                design.Id,
                design.ApplicationId,
                LoginDesignSnapshot.Of(design),
                request.DeactivatedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return LoginDesignDto.From(design);
    }
}
