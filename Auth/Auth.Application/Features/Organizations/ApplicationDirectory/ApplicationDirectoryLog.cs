using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// The one log line of a directory read (OI-194, T13): which application read what of which
/// organization, and how many items it received. Never an email or a name: the line says that a
/// read happened, not what it returned.
/// </summary>
internal static class ApplicationDirectoryLog
{
    public static void Read(ILogger logger, ApplicationDirectoryGrant grant, string endpoint, int itemCount) =>
        logger.LogInformation(
            "Application directory read: application {ApplicationCode} read {Endpoint} of organization {OrganizationId} ({ItemCount} items)",
            grant.Application.Code,
            endpoint,
            grant.Organization.Id,
            itemCount);
}
