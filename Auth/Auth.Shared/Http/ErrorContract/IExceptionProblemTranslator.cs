using Microsoft.AspNetCore.Http;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// Turns one exception type into a problem other than the default 500 (ADR 0001, section 5).
/// A host registers one per exception type it translates; <see cref="ErrorContractExceptionHandler"/>
/// picks it by the thrown exception's exact type.
/// </summary>
public interface IExceptionProblemTranslator
{
    /// <summary>The exception type this translator serves: the key it is chosen by.</summary>
    Type ExceptionType { get; }

    /// <summary>
    /// The problem for <paramref name="exception"/>, or <c>null</c> when this instance of the type
    /// is not one the translator recognizes: it then falls through to the outage classifier and
    /// then to the 500 problem.
    /// </summary>
    ExceptionProblem? Translate(Exception exception);
}

/// <summary>
/// The status an exception is answered with, and the code to record for it; a null
/// <see cref="Code"/> takes the transport code for <see cref="Status"/>.
/// </summary>
public sealed record ExceptionProblem(int Status, string? Code)
{
    /// <summary>A dependency outage: 503, <see cref="TransportErrorCodes.Unavailable"/>, <c>Retry-After</c>.</summary>
    public static ExceptionProblem Outage { get; } = new(StatusCodes.Status503ServiceUnavailable, null);
}
