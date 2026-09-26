namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// <c>HttpContext.Items</c> keys through which whoever decides an error tells the problem-details
/// customization which code to write (ADR 0001). The customization is the only author of
/// <c>code</c> and <c>detail</c>; producers set a status and record the code here.
/// </summary>
public static class ProblemItems
{
    /// <summary>
    /// The code of the error (<see cref="string"/>): a handler's first error, a middleware's
    /// reason, or an exception translation. Absent: the transport code for the status.
    /// </summary>
    public const string Code = "problem.code";

    /// <summary>
    /// Values for the placeholders of the code's sentence (<see cref="object"/>[]), in order.
    /// </summary>
    public const string Args = "problem.args";
}
