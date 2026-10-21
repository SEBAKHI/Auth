namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>
/// Body of <c>PUT api/v1/applications/{applicationId}/machine-keys/access</c> (OI-202).
/// </summary>
public sealed class SetMachineAccessRequest
{
    /// <summary>
    /// Whether the application's server, with its machine key and no user, may obtain the machine
    /// scope <c>organizations.read.all</c>. Absent reads as false, the side that withholds.
    /// </summary>
    public bool OrganizationsReadAll { get; init; }
}
