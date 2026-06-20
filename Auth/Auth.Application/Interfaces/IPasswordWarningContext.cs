namespace Auth.Application.Interfaces;

/// <summary>
/// A non-blocking warning about a password operation that still succeeded
/// (e.g. a breached password accepted under <c>BreachAction.Warn</c>).
/// </summary>
/// <param name="Code">A stable machine-readable code, e.g. <c>User.PasswordBreached</c>.</param>
/// <param name="Message">A human-readable default (English) message.</param>
public sealed record PasswordWarning(string Code, string Message);

/// <summary>
/// Request-scoped sink for non-blocking password warnings. Application code records warnings here;
/// the API layer surfaces them to the client (e.g. via an <c>X-Password-Warning</c> response header)
/// without changing handler return types or breaking 204 No Content responses.
/// </summary>
public interface IPasswordWarningContext
{
    /// <summary>The warnings recorded during the current request.</summary>
    IReadOnlyList<PasswordWarning> Warnings { get; }

    /// <summary>Records a non-blocking warning.</summary>
    void Add(PasswordWarning warning);
}
