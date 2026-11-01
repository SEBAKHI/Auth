using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.UpdateApplicationLogoHeight;

/// <summary>
/// Handles <see cref="UpdateApplicationLogoHeightCommand"/>. The range is the domain's
/// (<see cref="LogoHeight.CreateOptional"/>); saving the height already stored writes nothing and
/// raises no event. The answer is always what is stored.
/// </summary>
public sealed class UpdateApplicationLogoHeightCommandHandler
    : IRequestHandler<UpdateApplicationLogoHeightCommand, ErrorOr<ApplicationLogoHeightDto>>
{
    private readonly IApplicationRepository _applications;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateApplicationLogoHeightCommandHandler> _logger;

    public UpdateApplicationLogoHeightCommandHandler(
        IApplicationRepository applications,
        IPublisher publisher,
        TimeProvider time,
        ILogger<UpdateApplicationLogoHeightCommandHandler> logger)
    {
        _applications = applications;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationLogoHeightDto>> Handle(
        UpdateApplicationLogoHeightCommand request,
        CancellationToken cancellationToken)
    {
        var height = LogoHeight.CreateOptional(request.LogoHeightPx);
        if (height.IsError)
        {
            return height.Errors;
        }

        var application = await _applications.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var before = application.LogoHeight?.Px;
        if (application.SetLogoHeight(height.Value, request.ChangedBy))
        {
            if (!await _applications.UpdateLogoHeightAsync(application, cancellationToken))
            {
                // Deleted since it was read: nothing was written, and nothing is announced.
                return ApplicationErrors.NotFound(request.ApplicationId);
            }

            _logger.LogInformation(
                "Logo height of application {ApplicationId} set to {LogoHeightPx} by {ChangedBy}",
                application.Id, application.LogoHeight?.Px, request.ChangedBy);

            await _publisher.Publish(
                new ApplicationLogoHeightChangedEvent(
                    Guid.NewGuid(),
                    application.Id,
                    application.Code,
                    before,
                    application.LogoHeight?.Px,
                    request.ChangedBy,
                    _time.GetUtcNow()),
                CancellationToken.None);
        }

        return new ApplicationLogoHeightDto { LogoHeightPx = application.LogoHeight?.Px };
    }
}
