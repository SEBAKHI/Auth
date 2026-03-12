using Auth.Domain.Entities;

namespace Auth.Application.Interfaces;

/// <summary>
/// Shared service that creates a personal organization for a user.
/// Used by both RegisterCommandHandler and ExternalLoginCommandHandler.
/// </summary>
public interface IPersonalOrganizationCreator
{
    /// <summary>
    /// Creates a personal organization for the given user with the org-owner role.
    /// </summary>
    /// <param name="user">The user to create the organization for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if created successfully, false if the org-owner role was not found.</returns>
    Task<bool> CreateAsync(User user, CancellationToken cancellationToken = default);
}
