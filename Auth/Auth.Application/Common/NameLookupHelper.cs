using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;

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
