using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a permission in the hierarchical permission system.
/// Supports wildcard patterns (e.g., "crm:*", "crm:leads:*", "crm:leads:read").
/// </summary>
public class Permission : AuditableEntityBase
{
    /// <summary>
    /// Gets the ID of the application this permission belongs to.
    /// Null indicates a global permission applicable to all applications.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>
    /// Gets the permission code using colon-separated hierarchy (e.g., "crm:leads:read").
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the display name of the permission.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the description of the permission.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the parent permission ID for hierarchical organization.
    /// </summary>
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// Gets the hierarchy level: 0=global(*), 1=application, 2=resource, 3=action.
    /// </summary>
    public byte Level { get; private set; }

    /// <summary>
    /// Gets whether this permission is a wildcard permission (ends with :* or is just *).
    /// </summary>
    public bool IsWildcard { get; private set; }

    /// <summary>
    /// Gets whether this permission is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    private Permission() : base()
    {
    }

    public Permission(
        Guid id,
        Guid? applicationId,
        string code,
        string name,
        string? description,
        Guid? parentId,
        byte level,
        bool isWildcard,
        bool isActive,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        ApplicationId = applicationId;
        Code = code;
        Name = name;
        Description = description;
        ParentId = parentId;
        Level = level;
        IsWildcard = isWildcard;
        IsActive = isActive;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    public static Permission Create(
        Guid? applicationId,
        string code,
        string name,
        string? description,
        Guid? parentId,
        Guid createdBy)
    {
        var normalizedCode = code.ToLowerInvariant();
        var permission = new Permission
        {
            ApplicationId = applicationId,
            Code = normalizedCode,
            Name = name,
            Description = description,
            ParentId = parentId,
            Level = CalculateLevel(normalizedCode),
            IsWildcard = normalizedCode == "*" || normalizedCode.EndsWith(":*"),
            IsActive = true
        };
        permission.SetCreated(createdBy);
        return permission;
    }

    public void Update(
        string name,
        string? description,
        Guid modifiedBy)
    {
        Name = name;
        Description = description;
        SetModified(modifiedBy);
    }

    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetModified(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// Checks if this permission matches the required permission using wildcard logic.
    /// </summary>
    /// <param name="requiredPermission">The permission code to check against.</param>
    /// <returns>True if this permission grants access to the required permission.</returns>
    public bool Matches(string requiredPermission)
    {
        // Global wildcard grants everything
        if (Code == "*")
            return true;

        // Exact match
        if (string.Equals(Code, requiredPermission, StringComparison.OrdinalIgnoreCase))
            return true;

        // Wildcard matching (e.g., "crm:*" matches "crm:leads:read")
        if (Code.EndsWith(":*"))
        {
            var prefix = Code[..^2]; // Remove ":*"
            return requiredPermission.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(requiredPermission, prefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Gets the parent permission code (e.g., "crm:leads:read" -> "crm:leads:*").
    /// </summary>
    public string? GetParentCode()
    {
        if (Code == "*") return null;

        var lastColon = Code.LastIndexOf(':');
        if (lastColon <= 0) return "*";

        return Code[..lastColon] + ":*";
    }

    /// <summary>
    /// Calculates the hierarchy level based on the permission code.
    /// </summary>
    private static byte CalculateLevel(string code)
    {
        if (code == "*") return 0;
        return (byte)(code.Count(c => c == ':') + 1);
    }
}
