using Auth.Domain.Entities;
using Auth.Domain.Enums;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// A code presented as one second factor, with what it is presented for: the
/// account, and the challenge that asks for it.
/// </summary>
/// <param name="UserId">The account whose factor is presented.</param>
/// <param name="Method">The factor the code is presented as.</param>
/// <param name="Code">The code as the user typed it.</param>
/// <param name="Challenge">
/// The challenge the code completes: a sign-in's, or a recovery's. An emailed code is
/// emailed for one challenge and checked against it alone; its first factor decides
/// whether an emailed code may complete it at all.
/// </param>
public sealed record SecondFactorPresentation(
    Guid UserId,
    SecondFactorMethod Method,
    string Code,
    TwoFactorChallenge? Challenge)
{
    // The code is a secret the user typed; it stays out of every log line and
    // assertion message the presentation reaches.
    public override string ToString() =>
        $"SecondFactorPresentation {{ UserId = {UserId}, Method = {Method}, ChallengeId = {Challenge?.Id} }}";
}
