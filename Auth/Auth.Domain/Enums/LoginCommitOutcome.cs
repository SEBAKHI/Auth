namespace Auth.Domain.Enums;

/// <summary>
/// How the commit of a verified second-factor sign-in ended. The commit consumes
/// the challenge and settles the factor in one transaction, so every outcome but
/// <see cref="Committed"/> means nothing was written and no token may be issued.
/// </summary>
public enum LoginCommitOutcome
{
    /// <summary>The challenge was consumed and the factor settled together.</summary>
    Committed = 1,

    /// <summary>
    /// The challenge was already used — a concurrent request with a correct code
    /// won it, or a newer sign-in superseded it.
    /// </summary>
    ChallengeLost = 2,

    /// <summary>
    /// The factor no longer matched the proof — it was switched off, or the
    /// recovery code was spent by a concurrent sign-in on another challenge.
    /// </summary>
    FactorLost = 3
}
