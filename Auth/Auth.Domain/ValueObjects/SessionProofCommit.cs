namespace Auth.Domain.ValueObjects;

/// <summary>
/// One accepted proof inside a signed-in session, carried from the checks, which write nothing,
/// to the one transaction that spends it and stamps the session: an emailed code cannot be spent
/// without the session being stamped, nor the other way round.
/// </summary>
/// <remarks>
/// Spent in the order every commit takes its rows, so two commits never deadlock: the challenge
/// of a first proof by emailed code, then the second factor, then the session and its SSO session.
/// A password or a passkey has nothing left to spend here: the password has no single use, and the
/// passkey's challenge is spent before it is checked.
/// </remarks>
public sealed record class SessionProofCommit
{
    /// <param name="session">The session (and SSO session) to stamp, and what this proof proved.</param>
    /// <param name="firstProofCode">
    /// The emailed code that was the first proof, of an account without a password; null otherwise.
    /// </param>
    /// <param name="secondFactor">What the second step proved; null when the account has none.</param>
    public SessionProofCommit(
        SessionUpgrade session,
        EmailedCodeSpend? firstProofCode = null,
        SecondFactorProof? secondFactor = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        FirstProofCode = firstProofCode;
        SecondFactor = secondFactor;
    }

    /// <summary>Gets the session (and SSO session) to stamp, and what this proof proved.</summary>
    public SessionUpgrade Session { get; }

    /// <summary>Gets the emailed code that was the first proof; null when it was none.</summary>
    public EmailedCodeSpend? FirstProofCode { get; }

    /// <summary>Gets what the second step proved; null when the account has none.</summary>
    public SecondFactorProof? SecondFactor { get; }
}

/// <summary>
/// An emailed code to spend with its challenge, only while the challenge still holds the codes it
/// was checked against: a code a newer send replaced after the check proves nothing.
/// </summary>
public sealed record class EmailedCodeSpend
{
    /// <param name="challengeId">The challenge the code was emailed for.</param>
    /// <param name="codesSeen">That challenge's code hashes exactly as read.</param>
    public EmailedCodeSpend(Guid challengeId, string codesSeen)
    {
        ArgumentException.ThrowIfNullOrEmpty(codesSeen);

        ChallengeId = challengeId;
        CodesSeen = codesSeen;
    }

    /// <summary>Gets the challenge the code was emailed for.</summary>
    public Guid ChallengeId { get; }

    /// <summary>Gets that challenge's code hashes exactly as read.</summary>
    public string CodesSeen { get; }

    // Hashes of a live code stay out of log lines.
    public override string ToString() => $"EmailedCodeSpend {{ ChallengeId = {ChallengeId} }}";
}
