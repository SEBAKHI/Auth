using Auth.Domain.Primitives;
using Auth_API.Modules.AuditLog.EventHandlers;
using MediatR;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// Every domain event this system publishes has somewhere to land.
///
/// An event with no handler is not a compile error, not a warning, and not
/// visible at runtime: MediatR publishes it to an empty list and returns. That
/// is how WebhookKeyCreatedEvent and WebhookKeyRevokedEvent were raised from the
/// day the feature shipped while nothing ever wrote them down — the code looked
/// finished from either end, because the publisher published and the audit
/// module was full of handlers.
///
/// The same silence is what a new event walks into. This test is the noise.
/// </summary>
public class DomainEventCoverageTests
{
    /// <summary>
    /// Events that deliberately have no handler, each with the reason. An entry
    /// here is a decision that something need not be recorded or reacted to, so
    /// it costs an edit and a sentence rather than a silently passing test.
    /// </summary>
    private static readonly Dictionary<string, string> UnhandledOnPurpose = new(StringComparer.Ordinal);

    [Fact]
    public void EveryDomainEvent_HasAtLeastOneHandler()
    {
        var events = typeof(IDomainEvent).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(type))
            .ToList();

        events.Should().NotBeEmpty("the domain publishes events");

        var handledTypes = typeof(UserCreatedAuditEventHandler).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .SelectMany(type => type.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var orphans = events
            .Where(type => !handledTypes.Contains(type))
            .Select(type => type.Name)
            .Where(name => !UnhandledOnPurpose.ContainsKey(name))
            .OrderBy(name => name)
            .ToList();

        orphans.Should().BeEmpty(
            "an event nobody handles is raised into silence — the publisher looks correct, the audit "
            + "module looks complete, and the action leaves no record at all. Add a handler, or add the "
            + "event to UnhandledOnPurpose with the reason it needs none");
    }

    [Fact]
    public void EveryAuthorityChange_IsAudited()
    {
        // The events below are the ones that move authority between principals.
        // They are named individually rather than discovered, because the point
        // is to fail when a NEW one is added without a handler, and a discovery
        // rule can only find what already fits its pattern.
        var mustBeAudited = new[]
        {
            "UserPermissionGrantedEvent", "UserPermissionRevokedEvent",
            "RolePermissionGrantedEvent", "RolePermissionRevokedEvent",
            "RoleAssignedEvent", "UserRoleRemovedEvent",
            "RoleCreatedEvent", "RoleUpdatedEvent", "RoleDeletedEvent",
        };

        var auditHandlers = typeof(UserCreatedAuditEventHandler).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.Namespace == typeof(UserCreatedAuditEventHandler).Namespace)
            .SelectMany(type => type.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0].Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = mustBeAudited.Where(name => !auditHandlers.Contains(name)).ToList();

        missing.Should().BeEmpty(
            "these are the changes an incident investigation asks about first, and until recently not "
            + "one of them was written down: a permission grant, a role removal and a role deletion all "
            + "happened with no audit row at all");
    }
}
