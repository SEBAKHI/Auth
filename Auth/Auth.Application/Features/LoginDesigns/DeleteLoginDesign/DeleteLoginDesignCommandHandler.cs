using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.DeleteLoginDesign;

/// <summary>
/// Handles <see cref="DeleteLoginDesignCommand"/>. The entity refuses the active design, and the
/// delete itself only removes an inactive row, so a design activated between the read and the
/// delete is refused the same way.
/// </summary>
public sealed class DeleteLoginDesignCommandHandler : IRequestHandler<DeleteLoginDesignCommand, ErrorOr<Deleted>>
{
    private readonly ILoginDesignRepository _designs;
    private readonly LoginDesignScope _scope;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<DeleteLoginDesignCommandHandler> _logger;

    public DeleteLoginDesignCommandHandler(
        ILoginDesignRepository designs,
        LoginDesignScope scope,
        IPublisher publisher,
        TimeProvider time,
        ILogger<DeleteLoginDesignCommandHandler> logger)
    {
        _designs = designs;
        _scope = scope;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(DeleteLoginDesignCommand request, CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.ApplicationId, request.DesignId, cancellationToken);
        if (loaded.IsError)
        {
            return loaded.Errors;
        }

        var design = loaded.Value;
        var deletable = design.EnsureDeletable();
        if (deletable.IsError)
        {
            return deletable.Errors;
        }

        if (!await _designs.DeleteInactiveAsync(design.Id, cancellationToken))
        {
            // Activated (or already deleted) since it was read.
            var current = await _designs.GetByIdAsync(design.Id, cancellationToken);
            return current is null ? LoginDesignErrors.NotFound(design.Id) : LoginDesignErrors.DeleteActive;
        }

        _logger.LogInformation(
            "Sign-in page design {DesignId} deleted by {DeletedBy}", design.Id, request.DeletedBy);

        await _publisher.Publish(
            new LoginDesignDeletedEvent(
                Guid.NewGuid(),
                design.Id,
                design.ApplicationId,
                LoginDesignSnapshot.Of(design),
                request.DeletedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return Result.Deleted;
    }
}
