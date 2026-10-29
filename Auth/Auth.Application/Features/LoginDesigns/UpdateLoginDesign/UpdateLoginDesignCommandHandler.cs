using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.UpdateLoginDesign;

/// <summary>
/// Handles <see cref="UpdateLoginDesignCommand"/>. The save gate runs on what is being saved,
/// and the write goes through only while the stored row is still the revision the editor
/// loaded, so two administrators editing one design cannot overwrite each other unseen.
/// </summary>
public sealed class UpdateLoginDesignCommandHandler
    : IRequestHandler<UpdateLoginDesignCommand, ErrorOr<LoginDesignDto>>
{
    private readonly ILoginDesignRepository _designs;
    private readonly LoginDesignScope _scope;
    private readonly LoginDesignRenderer _renderer;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateLoginDesignCommandHandler> _logger;

    public UpdateLoginDesignCommandHandler(
        ILoginDesignRepository designs,
        LoginDesignScope scope,
        LoginDesignRenderer renderer,
        IPublisher publisher,
        TimeProvider time,
        ILogger<UpdateLoginDesignCommandHandler> logger)
    {
        _designs = designs;
        _scope = scope;
        _renderer = renderer;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginDesignDto>> Handle(
        UpdateLoginDesignCommand request,
        CancellationToken cancellationToken)
    {
        var loaded = await _scope.LoadAsync(request.ApplicationId, request.DesignId, cancellationToken);
        if (loaded.IsError)
        {
            return loaded.Errors;
        }

        var design = loaded.Value;
        if (!design.IsRevision(request.ExpectedRevision))
        {
            return LoginDesignErrors.ConcurrencyConflict;
        }

        var before = LoginDesignSnapshot.Of(design);
        var updated = design.Update(
            request.Name, request.Content, request.StringsJson, request.ShowPlatformLogo, request.ModifiedBy);
        if (updated.IsError)
        {
            return updated.Errors;
        }

        var audience = await _renderer.AudienceAsync(design.ApplicationId, cancellationToken);
        if (audience is null)
        {
            return ApplicationErrors.NotFound(design.ApplicationId!.Value);
        }

        var gate = _renderer.CheckForSave(design.Content, design.StringsJson, audience);
        if (gate.IsError)
        {
            return gate.Errors;
        }

        if (!await _designs.TryUpdateAsync(design, request.ExpectedRevision, cancellationToken))
        {
            // Saved by someone else (or deleted) between the read and this write.
            return LoginDesignErrors.ConcurrencyConflict;
        }

        _logger.LogInformation(
            "Sign-in page design {DesignId} saved by {ModifiedBy} (active: {IsActive})",
            design.Id, request.ModifiedBy, design.IsActive);

        await _publisher.Publish(
            new LoginDesignUpdatedEvent(
                Guid.NewGuid(),
                design.Id,
                design.ApplicationId,
                design.IsActive,
                before,
                LoginDesignSnapshot.Of(design),
                request.ModifiedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return LoginDesignDto.From(design);
    }
}
