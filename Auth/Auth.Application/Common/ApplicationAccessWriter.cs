using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Common;

/// <summary>
/// The one writer of a user's access grant to an application: inviting someone from
/// the application page, and granting a role or a permission inside an application
/// from the user page, which carries the access with it.
/// </summary>
/// <remarks>
/// <para>
/// A role or a permission scoped to an application is authority inside it, and a
/// Restricted application admits only an account with a valid access grant. Writing
/// the authority without the access left a user holding a role they could not use:
/// a silent dead end. So every grant at an application's scope writes the access
/// too, when the user has no valid one, the way the application page always did.
/// </para>
/// <para>
/// The unique (application, user) constraint means a previously revoked or lapsed
/// grant is reinstated on its own row, not inserted beside itself, which also keeps
/// the earlier trial in the audit trail.
/// </para>
/// </remarks>
public class ApplicationAccessWriter
{
    private readonly IApplicationAccessRepository _accessRepository;
    private readonly IPublisher _publisher;

    public ApplicationAccessWriter(IApplicationAccessRepository accessRepository, IPublisher publisher)
    {
        _accessRepository = accessRepository;
        _publisher = publisher;
    }

    /// <summary>The user's grant to the application, valid or not; null when there never was one.</summary>
    public Task<ApplicationUserAccess?> GetAsync(
        Guid applicationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        _accessRepository.GetGrantAsync(applicationId, userId, cancellationToken);

    /// <summary>
    /// Writes the grant: reinstates <paramref name="existing"/> when there is one,
    /// creates it otherwise. Publishing is the caller's next step
    /// (<see cref="PublishGrantedAsync"/>), so a command that writes more after the
    /// access keeps its own order.
    /// </summary>
    public async Task WriteAsync(
        ApplicationEntity application,
        Guid userId,
        ApplicationUserAccess? existing,
        Guid grantedBy,
        DateTime? expiresAt,
        string? note,
        CancellationToken cancellationToken)
    {
        if (existing is not null)
        {
            existing.Reinstate(grantedBy, expiresAt, note);
            await _accessRepository.UpdateGrantAsync(existing, cancellationToken);
            return;
        }

        var grant = ApplicationUserAccess.Create(application.Id, userId, grantedBy, expiresAt, note);
        await _accessRepository.CreateGrantAsync(grant, cancellationToken);
    }

    /// <summary>Records that the user was given access to the application.</summary>
    public Task PublishGrantedAsync(
        ApplicationEntity application,
        Guid userId,
        DateTime? expiresAt,
        Guid grantedBy,
        CancellationToken cancellationToken) =>
        _publisher.Publish(
            new ApplicationAccessGrantedEvent(application.Id, application.Code, userId, expiresAt, grantedBy),
            cancellationToken);

    /// <summary>
    /// For a grant at the application's scope: writes the access and publishes it when
    /// the user has no valid grant, and leaves a valid one untouched, its expiry
    /// included. Returns whether it wrote.
    /// </summary>
    /// <remarks>Called after every check of the grant it accompanies, before that grant is written.</remarks>
    public async Task<bool> EnsureAsync(
        ApplicationEntity application,
        Guid userId,
        Guid grantedBy,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(application.Id, userId, cancellationToken);
        if (existing is not null && existing.IsValid())
        {
            return false;
        }

        await WriteAsync(application, userId, existing, grantedBy, expiresAt, note: null, cancellationToken);
        await PublishGrantedAsync(application, userId, expiresAt, grantedBy, cancellationToken);
        return true;
    }
}
