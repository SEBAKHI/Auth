using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.SetApplicationActive;

/// <summary>
/// Switches an application on or off. Off locks everyone out — invited users
/// and organization members alike — regardless of its access mode.
/// </summary>
/// <remarks>
/// Deliberately not a field on the update contract. The console's update is a
/// full-object PUT assembled from possibly stale client state, so a bystander
/// action such as uploading a logo could otherwise resurrect an application an
/// administrator had just switched off. A security switch must not be flippable
/// by accident, and it earns its own audit line.
/// <para>
/// One command with a boolean rather than two near-identical ones: the flag
/// selects which entity behavior to invoke, and two handlers would be the same
/// body twice.
/// </para>
/// </remarks>
public record SetApplicationActiveCommand(
    Guid Id,
    bool IsActive) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the administrator flipping the switch (for audit).
    /// </summary>
    public Guid ModifiedBy { get; init; }
}
