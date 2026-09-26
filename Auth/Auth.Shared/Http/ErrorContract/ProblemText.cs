using System.Globalization;
using Auth_Localization.Resources.Errors;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// The one place a problem's <c>detail</c> is produced: the sentence published for its code,
/// in the request's UI culture (ADR 0001). Every published code, catalog and transport alike,
/// has a sentence in <see cref="DomainErrors"/> in every supported culture.
/// </summary>
public sealed class ProblemText
{
    private readonly IStringLocalizer<DomainErrors> _sentences;
    private readonly ILogger<ProblemText> _logger;

    public ProblemText(IStringLocalizer<DomainErrors> sentences, ILogger<ProblemText> logger)
    {
        _sentences = sentences;
        _logger = logger;
    }

    /// <summary>
    /// The sentence for <paramref name="code"/> with <paramref name="args"/> in its placeholders,
    /// or <c>null</c> when the code has no sentence or the arguments do not fit it. A problem
    /// without <c>detail</c> is still complete; a sentence with a raw <c>{0}</c> is not.
    /// </summary>
    public string? Describe(string code, object[]? args)
    {
        var sentence = _sentences[code];
        if (sentence.ResourceNotFound)
        {
            _logger.LogWarning("Error code {ErrorCode} has no published sentence", code);
            return null;
        }

        if (args is not { Length: > 0 })
        {
            return sentence.Value;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, sentence.Value, args);
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(exception,
                "The {Culture} sentence of error code {ErrorCode} does not fit its {ArgumentCount} arguments",
                CultureInfo.CurrentUICulture.Name, code, args.Length);
            return null;
        }
    }
}
