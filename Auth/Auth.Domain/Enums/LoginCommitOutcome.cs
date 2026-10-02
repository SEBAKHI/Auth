namespace Auth.Domain.Enums;

/// <summary>
/// How the commit of a verified second-factor code ended. A sign-in consumes the
/// challenge and settles the factor in one transaction; switching the factor on
/// or off writes the factor row and the account flag in one transaction; account
/// recovery claims the code's time step alone. Every outcome but
/// <see cref="Committed"/> and <see cref="ReuseAccepted"/> means nothing was
/// written and the code must not count.
/// </summary>
/// <remarks>
/// The values are fixed: a later outcome takes the next free number.
/// </remarks>
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
    FactorLost = 3,

    /// <summary>
    /// The authenticator-app code was correct, but its time step was not newer
    /// than the last one accepted: the same code, or an older one, presented
    /// again. Nothing was written; the attempt stays counted.
    /// </summary>
    StepReused = 4,

    /// <summary>
    /// The code reused an accepted time step and was let through anyway, because
    /// <c>TwoFactor:RejectReusedCodes</c> is off. A success: the factor was
    /// settled. Exists only as long as that rollout switch does.
    /// </summary>
    ReuseAccepted = 5,

    /// <summary>
    /// Switching the factor on lost to a concurrent request that switched it on
    /// first. Nothing was written, and the codes this request generated are never
    /// shown: they are not the ones stored.
    /// </summary>
    AlreadyEnabled = 6,

    /// <summary>
    /// The recovery code was correct when checked, but the stored set changed
    /// before the commit — a concurrent sign-in spent a code. Nothing was written.
    /// </summary>
    RecoveryCodesChanged = 7
}
