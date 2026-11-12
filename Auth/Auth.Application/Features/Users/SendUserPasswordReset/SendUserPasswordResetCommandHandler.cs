using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.SendUserPasswordReset;

/// <summary>
/// Handles <see cref="SendUserPasswordResetCommand"/> through the one issuer every reset link goes
/// through, and records who asked when a link was issued.
/// </summary>
public sealed class SendUserPasswordResetCommandHandler
    : IRequestHandler<SendUserPasswordResetCommand, ErrorOr<PasswordResetLinkResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetLinkIssuer _linkIssuer;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;

    public SendUserPasswordResetCommandHandler(
        IUserRepository userRepository,
        IPasswordResetLinkIssuer linkIssuer,
        IPublisher publisher,
        TimeProvider timeProvider)
    {
        _userRepository = userRepository;
        _linkIssuer = linkIssuer;
        _publisher = publisher;
        _timeProvider = timeProvider;
    }

    public async Task<ErrorOr<PasswordResetLinkResponse>> Handle(
        SendUserPasswordResetCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var issue = await _linkIssuer.IssueAsync(user, cancellationToken);
        if (issue.Issued)
        {
            // After the writes, and not cancellable by a client that went away: the link exists.
            await _publisher.Publish(
                new PasswordResetLinkIssuedEvent(Guid.NewGuid(), user.Id, request.RequestedBy, _timeProvider.GetUtcNow()),
                CancellationToken.None);
        }

        return new PasswordResetLinkResponse(issue.ExpiresAt, EmailMasking.Mask(user.Email));
    }
}
