using Auth.Domain.Entities;
using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a sign-in page design was created, from scratch or as a copy. It starts
/// inactive, so no visitor sees it yet.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="LoginDesignId">The new design.</param>
/// <param name="ApplicationId">Its application; null for one of the platform's designs.</param>
/// <param name="Design">The new design's name, logo choice and version.</param>
/// <param name="CopiedFromId">The design it was copied from; null when created from scratch.</param>
/// <param name="TriggeredBy">The administrator who created it.</param>
/// <param name="OccurredAt">When it was stored.</param>
public sealed record LoginDesignCreatedEvent(
    Guid EventId,
    Guid LoginDesignId,
    Guid? ApplicationId,
    LoginDesignSnapshot Design,
    Guid? CopiedFromId,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>A sign-in page design at one moment, as plain values: what an audit row records of it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="ShowPlatformLogo">Whether the pages keep the platform's mark in the corner.</param>
/// <param name="ContentHash">The SHA-256 of its content and strings: which version it was.</param>
public sealed record LoginDesignSnapshot(string Name, bool ShowPlatformLogo, string ContentHash)
{
    /// <summary>The snapshot of <paramref name="design"/>.</summary>
    public static LoginDesignSnapshot Of(LoginDesign design) =>
        new(design.Name, design.ShowPlatformLogo, design.ContentHash);
}
