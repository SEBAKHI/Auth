namespace Auth.Domain.Primitives;

/// <summary>
/// Base class for entities that require audit tracking.
/// </summary>
public abstract class AuditableEntityBase : EntityBase, IAuditableEntity
{
    public DateTime CreatedAt { get; protected set; }
    public Guid CreatedBy { get; protected set; }
    public DateTime? ModifiedAt { get; protected set; }
    public Guid? ModifiedBy { get; protected set; }

    protected AuditableEntityBase() : base()
    {
    }

    protected AuditableEntityBase(Guid id) : base(id)
    {
    }

    public void SetCreated(Guid userId)
    {
        CreatedAt = DateTime.UtcNow;
        CreatedBy = userId;
    }

    public void SetModified(Guid userId)
    {
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = userId;
    }
}
