namespace Auth.Domain.Enums;

/// <summary>
/// Which side of an audit row a person has to be on to match a participant filter.
/// </summary>
/// <remarks>
/// <para>
/// Every audit row names two people — who performed the action and who it
/// happened to — and they coincide only when someone acts on their own account.
/// A filter that names one person therefore has to say which of the two it
/// means. Until this existed the only answer available was the subject, so
/// "everything this account did" was a question the trail could not be asked.
/// </para>
/// <para>
/// There is no default here on purpose. A role that silently widens is the same
/// defect as a filter that silently does nothing, and the request validators
/// reject a participant id that arrives without one.
/// </para>
/// </remarks>
public enum AuditParticipantRole
{
    /// <summary>Rows where the action happened TO this person (<c>UserId</c>).</summary>
    Subject = 0,

    /// <summary>Rows where this person PERFORMED the action (<c>PerformedBy</c>).</summary>
    Actor = 1,

    /// <summary>Rows where this person is on either side.</summary>
    Either = 2
}
