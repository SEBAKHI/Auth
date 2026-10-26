using Auth.Domain.Constants;

namespace Auth.Application.DTOs;

/// <summary>
/// What a sign-in by emailed code does next, once its code matched (OI-215). Answered
/// by the verify step, and by the sign-in and recovery steps when they issue no
/// session; never before the code was checked.
/// </summary>
/// <param name="Step">One of <see cref="EmailCodeSteps"/>.</param>
/// <param name="PasswordRequired">
/// For <see cref="EmailCodeSteps.CompleteAccount"/>: whether the new account must set a
/// password. Null — and then not written — for every other step.
/// </param>
/// <param name="NameRequired">
/// For <see cref="EmailCodeSteps.CompleteAccount"/>: whether it must give a first and
/// last name. Null for every other step.
/// </param>
public sealed record EmailCodeNextStep(string Step, bool? PasswordRequired = null, bool? NameRequired = null)
{
    /// <summary>The address has an account.</summary>
    public static EmailCodeNextStep SignIn { get; } = new(EmailCodeSteps.SignIn);

    /// <summary>The address's account waits for deletion.</summary>
    public static EmailCodeNextStep Recover { get; } = new(EmailCodeSteps.Recover);

    /// <summary>The account must prove its password to finish.</summary>
    public static EmailCodeNextStep PasswordRequiredToFinish { get; } = new(EmailCodeSteps.PasswordRequired);

    /// <summary>The address has no account; the new one needs these fields.</summary>
    public static EmailCodeNextStep CompleteAccount(bool passwordRequired, bool nameRequired) =>
        new(EmailCodeSteps.CompleteAccount, passwordRequired, nameRequired);
}
