namespace Auth.Application.DTOs;

/// <summary>
/// The part of the password policy a person needs BEFORE choosing a password:
/// the composition rules a form can check while they type. It is served
/// anonymously, so it carries nothing else from
/// <see cref="Configuration.PasswordSettings"/>. The lock-out threshold, the
/// hashing parameters, the pepper and the breach-check settings describe how
/// the server defends itself, not what a password must look like, and
/// PasswordPolicyDisclosureTests holds this type to exactly these members.
/// <para>
/// The server remains the authority. A password that satisfies every rule
/// here can still be refused: common patterns, breached passwords and password
/// history are judged only on submit, and each localized reason comes back in
/// the ProblemDetails <c>errors</c> array.
/// </para>
/// </summary>
public sealed class PasswordPolicyDto
{
    /// <summary>Fewest characters accepted (<c>Password:MinimumLength</c>).</summary>
    public required int MinimumLength { get; init; }

    /// <summary>Whether at least one ASCII uppercase letter (A–Z) is required.</summary>
    public required bool RequireUppercase { get; init; }

    /// <summary>Whether at least one ASCII lowercase letter (a–z) is required.</summary>
    public required bool RequireLowercase { get; init; }

    /// <summary>Whether at least one digit (0–9) is required.</summary>
    public required bool RequireDigit { get; init; }

    /// <summary>Whether at least one ASCII punctuation symbol is required.</summary>
    public required bool RequireSpecialCharacter { get; init; }
}
