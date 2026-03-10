namespace Auth.Domain.Primitives;

/// <summary>
/// Interface for entities that track creation and modification metadata.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>
    /// Gets the UTC timestamp when the entity was created.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Gets the identifier of the user who created the entity.
    /// </summary>
    Guid CreatedBy { get; }

    /// <summary>
    /// Gets the UTC timestamp when the entity was last modified.
    /// </summary>
    DateTime? ModifiedAt { get; }

    /// <summary>
    /// Gets the identifier of the user who last modified the entity.
    /// </summary>
    Guid? ModifiedBy { get; }

    /// <summary>
    /// Sets the creation audit fields.
    /// </summary>
    void SetCreated(Guid userId);

    /// <summary>
    /// Sets the modification audit fields.
    /// </summary>
    void SetModified(Guid userId);
}
