using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Common;

/// <summary>
/// Resolves audit/reference identifiers to display names for DTO enrichment,
/// batching the lookups so list handlers stay one-round-trip per entity type.
/// </summary>
public static class NameLookupHelper
{
    /// <summary>
    /// Display name for a user: the explicit display name when set, otherwise
    /// "First Last" (the convention used across audit-log and organization DTOs).
    /// </summary>
    public static string DisplayName(User user) =>
        user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim();

    /// <summary>
    /// Resolves the distinct non-null <paramref name="userIds"/> to display
    /// names in a single repository call. Unknown ids are absent from the map.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(
        IUserRepository userRepository,
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await userRepository.GetByIdsAsync(ids, cancellationToken);
        return users is null
            ? new Dictionary<Guid, string>()
            : users.ToDictionary(user => user.Id, DisplayName);
    }

    /// <summary>
    /// The distinct non-null <paramref name="applicationIds"/>' applications, soft-deleted
    /// ones included, in ONE read: for a list that shows owner names and must also know
    /// which owners are deleted. Unknown ids are absent from the map.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, ApplicationEntity>> ApplicationsIncludingDeletedAsync(
        IApplicationRepository applicationRepository,
        IEnumerable<Guid?> applicationIds,
        CancellationToken cancellationToken)
    {
        var ids = applicationIds
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ApplicationEntity>();
        }

        var applications = await applicationRepository.GetByIdsIncludingDeletedAsync(ids, cancellationToken);
        return applications.ToDictionary(application => application.Id);
    }

    /// <summary>
    /// Whether an item owned by <paramref name="owner"/> can still be granted: an item
    /// with no owner always can; one whose owner application is deleted or gone cannot,
    /// since granting it answers <c>Application.NotFound</c>.
    /// </summary>
    public static bool OwnerIsLive(Guid? owner, IReadOnlyDictionary<Guid, ApplicationEntity> applications) =>
        owner is not { } id || (applications.TryGetValue(id, out var application) && !application.IsDeleted);

    /// <summary>
    /// Resolves the distinct non-null <paramref name="applicationIds"/> to
    /// application names. Unknown ids are absent from the map.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ApplicationNamesAsync(
        IApplicationRepository applicationRepository,
        IEnumerable<Guid?> applicationIds,
        CancellationToken cancellationToken)
    {
        var ids = applicationIds
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var names = new Dictionary<Guid, string>();
        foreach (var id in ids)
        {
            // Including soft-deleted apps: historical rows must keep resolving
            // the application name after the application is deleted.
            var application = await applicationRepository.GetByIdIncludingDeletedAsync(id, cancellationToken);
            if (application != null)
            {
                names[id] = application.Name;
            }
        }

        return names;
    }
}
