using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The one log line of a directory read (OI-194, T13): which application read what of which
/// organization, as which kind of caller (a user through it, or the application by itself with
/// its machine key, OI-202), and how many items it received. Never an email or a name: the line
/// says that a read happened, not what it returned.
/// </summary>
internal static class ApplicationDirectoryLog
{
    public static void Read(ILogger logger, ApplicationDirectoryGrant grant, string endpoint, int itemCount) =>
        logger.LogInformation(
            "Application directory read: application {ApplicationCode} read {Endpoint} of organization {OrganizationId} as {CallerKind} ({ItemCount} items)",
            grant.Application.Code,
            endpoint,
            grant.Organization.Id,
            grant.Authority.CallerKind,
            itemCount);
}
