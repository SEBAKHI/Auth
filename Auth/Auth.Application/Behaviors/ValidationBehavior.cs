using Auth.Domain.Constants;
using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Auth.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs FluentValidation validators before the handler executes.
/// If validation fails, short-circuits the pipeline and returns ErrorOr errors without calling the handler.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IErrorOr
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        // The code is the rule's catalog code (WithErrorCode), never the property
        // name, so one code means one rule on every endpoint (ADR 0001). The
        // property travels in metadata for the API to turn into a pointer, and
        // the message stays for logs. Failures keep rule-declaration order: the
        // first one is the primary error.
        var errors = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .DistinctBy(f => (f.ErrorCode, f.PropertyName))
            .Select(ToError)
            .ToList();

        if (errors.Count > 0)
        {
            return (dynamic)errors;
        }

        return await next();
    }

    private static Error ToError(ValidationFailure failure) =>
        Error.Validation(
            code: failure.ErrorCode,
            description: failure.ErrorMessage,
            metadata: string.IsNullOrEmpty(failure.PropertyName)
                ? null
                : new Dictionary<string, object> { [ErrorMetadataKeys.Property] = failure.PropertyName });
}
