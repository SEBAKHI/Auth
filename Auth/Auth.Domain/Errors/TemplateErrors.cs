using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors of the limits every Liquid render runs under, whichever kind of template it
/// is: notification templates and layouts, and sign-in page designs.
/// </summary>
/// <remarks>
/// A sign-in design over its size reports its own <see cref="LoginDesignErrors.RenderedTooLarge"/>
/// instead of <see cref="OutputTooLarge"/>: one code per published size, decided by the owner.
/// </remarks>
public static class TemplateErrors
{
    /// <summary>The code of <see cref="OutputTooLarge"/>, for a caller that reports the size its own way.</summary>
    public const string OutputTooLargeCode = "Template.OutputTooLarge";

    /// <summary>
    /// The rendered content is larger than its cap, in UTF-8 bytes counted after encoding. The
    /// render stops at the cap, so the excess is never built.
    /// </summary>
    public static Error OutputTooLarge(int maxBytes) => Error.Validation(
        code: OutputTooLargeCode,
        description: $"The rendered content is larger than {maxBytes} bytes.",
        metadata: new() { ["args"] = new object[] { maxBytes } });

    /// <summary>
    /// The template needs more than a render may spend: it nests too deeply, runs too long, or
    /// builds or reads more text than its output could hold.
    /// </summary>
    public static readonly Error TooComplex = Error.Validation(
        code: "Template.TooComplex",
        description: "The template is too complex to render: it nests too deeply, runs too long, or builds too much text.");
}
