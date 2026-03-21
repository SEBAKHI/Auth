using MediatR;

namespace Auth.Domain.Primitives;

/// <summary>
/// Marker interface for domain events raised by aggregate roots.
/// </summary>
public interface IDomainEvent : INotification
{
}
