using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.CopyLoginDesign;

/// <summary>Handles <see cref="CopyLoginDesignCommand"/>.</summary>
/// <remarks>
/// The copy is not put through the save gate again: its content and strings are the original's,
/// which passed it when they were saved.
/// </remarks>
public sealed class CopyLoginDesignCommandHandler
    : IRequestHandler<CopyLoginDesignCommand, ErrorOr<LoginDesignDto>>
{
    private readonly ILoginDesignRepository _designs;
    private readonly LoginDesignScope _scope;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<CopyLoginDesignCommandHandler> _logger;

    public CopyLoginDesignCommandHandler(
        ILoginDesignRepository designs,
        LoginDesignScope scope,
        IPublisher publisher,
        TimeProvider time,
        ILogger<CopyLoginDesignCommandHandler> logger)
    {
        _designs = designs;
        _scope = scope;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginDesignDto>> Handle(
        CopyLoginDesignCommand request,
        CancellationToken cancellationToken)
    {
        var original = await _scope.LoadAsync(request.ApplicationId, request.DesignId, cancellationToken);
        if (original.IsError)
        {
            return original.Errors;
        }

        var copied = original.Value.Copy(request.Name, request.CreatedBy);
        if (copied.IsError)
        {
            return copied.Errors;
        }

        var copy = copied.Value;
        await _designs.CreateAsync(copy, cancellationToken);

        _logger.LogInformation(
            "Sign-in page design {DesignId} copied from {OriginalId} by {CreatedBy}",
            copy.Id, request.DesignId, request.CreatedBy);

        await _publisher.Publish(
            new LoginDesignCreatedEvent(
                Guid.NewGuid(),
                copy.Id,
                copy.ApplicationId,
                LoginDesignSnapshot.Of(copy),
                CopiedFromId: request.DesignId,
                request.CreatedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return LoginDesignDto.From(copy);
    }
}
