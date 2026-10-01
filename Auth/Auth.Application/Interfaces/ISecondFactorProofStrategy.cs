using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Checks a code presented as one particular second factor. One implementation per
/// <see cref="SecondFactorMethod"/>; the verifier picks it by <see cref="Method"/>.
/// </summary>
public interface ISecondFactorProofStrategy
{
    /// <summary>
    /// Gets the factor this strategy checks.
    /// </summary>
    SecondFactorMethod Method { get; }

    /// <summary>
    /// Checks the code against the factor as read. Writes nothing.
    /// </summary>
    /// <param name="snapshot">The user's factor as the reservation read it.</param>
    /// <param name="code">The code as the user typed it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The proof to commit, or the error that rejected the code.</returns>
    Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        TwoFactorSnapshot snapshot,
        string code,
        CancellationToken cancellationToken);
}
