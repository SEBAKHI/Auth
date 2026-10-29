using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.CreateLoginDesign;

/// <summary>
/// Handles <see cref="CreateLoginDesignCommand"/>: the save gate runs before anything is stored
/// (the Liquid parses, every variable is one a sign-in page supplies), then the inactive design
/// is stored and announced.
/// </summary>
public sealed class CreateLoginDesignCommandHandler
    : IRequestHandler<CreateLoginDesignCommand, ErrorOr<LoginDesignDto>>
{
    private readonly ILoginDesignRepository _designs;
    private readonly LoginDesignRenderer _renderer;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<CreateLoginDesignCommandHandler> _logger;

    public CreateLoginDesignCommandHandler(
        ILoginDesignRepository designs,
        LoginDesignRenderer renderer,
        IPublisher publisher,
        TimeProvider time,
        ILogger<CreateLoginDesignCommandHandler> logger)
    {
        _designs = designs;
        _renderer = renderer;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginDesignDto>> Handle(
        CreateLoginDesignCommand request,
        CancellationToken cancellationToken)
    {
        var audience = await _renderer.AudienceAsync(request.ApplicationId, cancellationToken);
        if (audience is null)
        {
            return Auth.Domain.Errors.ApplicationErrors.NotFound(request.ApplicationId!.Value);
        }

        var fromStarter = request.Content is null;
        var content = request.Content ?? LoginDesignStarter.Content;
        var strings = fromStarter && request.StringsJson is null ? LoginDesignStarter.StringsJson : request.StringsJson;

        var created = LoginDesign.Create(
            request.ApplicationId, request.Name, content, strings, request.ShowPlatformLogo, request.CreatedBy);
        if (created.IsError)
        {
            return created.Errors;
        }

        var design = created.Value;
        var gate = _renderer.CheckForSave(design.Content, design.StringsJson, audience);
        if (gate.IsError)
        {
            return gate.Errors;
        }

        await _designs.CreateAsync(design, cancellationToken);

        _logger.LogInformation(
            "Sign-in page design {DesignId} created for {Scope} by {CreatedBy}",
            design.Id, request.ApplicationId?.ToString() ?? "the platform", request.CreatedBy);

        await _publisher.Publish(
            new LoginDesignCreatedEvent(
                Guid.NewGuid(),
                design.Id,
                design.ApplicationId,
                LoginDesignSnapshot.Of(design),
                CopiedFromId: null,
                request.CreatedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return LoginDesignDto.From(design);
    }
}
