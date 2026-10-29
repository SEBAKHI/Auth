using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.UpdateApplicationAppearance;

/// <summary>
/// Handles <see cref="UpdateApplicationAppearanceCommand"/>. A theme sent is held to the
/// platform's own rules (<see cref="PlatformTheme.Create"/>); saving what is already stored
/// writes nothing and raises no event. The answer is always what is stored.
/// </summary>
public sealed class UpdateApplicationAppearanceCommandHandler
    : IRequestHandler<UpdateApplicationAppearanceCommand, ErrorOr<ApplicationAppearanceDto>>
{
    private readonly IApplicationRepository _applications;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateApplicationAppearanceCommandHandler> _logger;

    public UpdateApplicationAppearanceCommandHandler(
        IApplicationRepository applications,
        IPublisher publisher,
        TimeProvider time,
        ILogger<UpdateApplicationAppearanceCommandHandler> logger)
    {
        _applications = applications;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationAppearanceDto>> Handle(
        UpdateApplicationAppearanceCommand request,
        CancellationToken cancellationToken)
    {
        PlatformTheme? theme = null;
        if (request.Theme is not null)
        {
            var created = request.Theme.ToTheme();
            if (created.IsError)
            {
                return created.Errors;
            }

            theme = created.Value;
        }

        var application = await _applications.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var before = ApplicationAppearanceSnapshot.Of(application.ThemeEnabled, application.Theme);
        var changed = application.SetAppearance(request.Enabled, theme, request.ChangedBy);
        if (changed.IsError)
        {
            return changed.Errors;
        }

        if (changed.Value)
        {
            if (!await _applications.UpdateAppearanceAsync(application, cancellationToken))
            {
                // Deleted since it was read: nothing was written, and nothing is announced.
                return ApplicationErrors.NotFound(request.ApplicationId);
            }

            _logger.LogInformation(
                "Appearance of application {ApplicationId} saved, switched {State}, by {ChangedBy}",
                application.Id, application.ThemeEnabled ? "on" : "off", request.ChangedBy);

            await _publisher.Publish(
                new ApplicationAppearanceChangedEvent(
                    Guid.NewGuid(),
                    application.Id,
                    application.Code,
                    before,
                    ApplicationAppearanceSnapshot.Of(application.ThemeEnabled, application.Theme),
                    request.ChangedBy,
                    _time.GetUtcNow()),
                CancellationToken.None);
        }

        return new ApplicationAppearanceDto
        {
            Enabled = application.ThemeEnabled,
            Theme = PlatformThemeDto.FromOrNull(application.Theme),
        };
    }
}
