using Auth_Lib.Foundation.Base;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents a permission scope granted to an API key.
/// </summary>
public class ApiKeyScope : EntityBase
{
    /// <summary>
    /// Gets the ID of the API key.
    /// </summary>
    public Guid ApiKeyId { get; private set; }

    /// <summary>
    /// Gets the ID of the permission.
    /// </summary>
    public Guid PermissionId { get; private set; }

    /// <summary>
    /// Gets the timestamp when the scope was granted.
    /// </summary>
    public DateTime GrantedAt { get; private set; }

    /// <summary>
    /// Gets the ID of the user who granted the scope.
    /// </summary>
    public Guid GrantedBy { get; private set; }

    private ApiKeyScope() : base()
    {
    }

    public ApiKeyScope(
        Guid id,
        Guid apiKeyId,
        Guid permissionId,
        DateTime grantedAt,
        Guid grantedBy) : base(id)
    {
        ApiKeyId = apiKeyId;
        PermissionId = permissionId;
        GrantedAt = grantedAt;
        GrantedBy = grantedBy;
    }

    public static ApiKeyScope Create(
        Guid apiKeyId,
        Guid permissionId,
        Guid grantedBy)
    {
        return new ApiKeyScope
        {
            ApiKeyId = apiKeyId,
            PermissionId = permissionId,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = grantedBy
        };
    }
}
