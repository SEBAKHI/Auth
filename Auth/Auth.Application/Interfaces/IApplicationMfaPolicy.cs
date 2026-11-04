using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Interfaces;

/// <summary>
/// The rule of an application that requires two-step verification (S23, AM2): what a
/// sign-in session must still do before the application may be issued a code, and
/// whether a code exchange or a refresh may go on. Separate from
/// <see cref="IPlatformMfaPolicy"/>, which decides the platform's own authority and never
/// applies to an application's token.
/// </summary>
/// <remarks>
/// Nothing here makes two-step verification mandatory: the application's flag is the
/// administrator's choice, off by default. With the flag off every answer is "nothing
/// more", and the account's factors are never read.
/// </remarks>
public interface IApplicationMfaPolicy
{
    /// <summary>
    /// The pure rule (AM2), in this order: the flag off is <see cref="ApplicationMfaRequirement.None"/>;
    /// an account without a qualifying factor is <see cref="ApplicationMfaRequirement.Enroll"/>,
    /// even for a session that proved one since removed; a session that proved two factors
    /// (<see cref="AuthenticationMethods.IsMfaSatisfied"/>) is <see cref="ApplicationMfaRequirement.None"/>;
    /// a first factor with an authenticator app on the account is
    /// <see cref="ApplicationMfaRequirement.StepUp"/>; anything else, nothing recorded or an
    /// emailed code of an account that has a password, is
    /// <see cref="ApplicationMfaRequirement.Reauthenticate"/>.
    /// </summary>
    /// <param name="requireTwoFactor">The application's flag.</param>
    /// <param name="methods">What the session proved; <see cref="AuthenticationMethods.Unknown"/> when nothing was recorded.</param>
    /// <param name="hasQualifyingFactor">
    /// Whether the account holds a qualifying second factor: an enabled authenticator app.
    /// An emailed code never qualifies.
    /// </param>
    ApplicationMfaRequirement Evaluate(bool requireTwoFactor, AuthenticationMethods methods, bool hasQualifyingFactor);

    /// <summary>
    /// <see cref="Evaluate"/> for a user, reading the account's factors only when the flag
    /// is on.
    /// </summary>
    Task<ApplicationMfaRequirement> EvaluateAsync(
        bool requireTwoFactor, Guid userId, AuthenticationMethods methods, CancellationToken cancellationToken);
}
