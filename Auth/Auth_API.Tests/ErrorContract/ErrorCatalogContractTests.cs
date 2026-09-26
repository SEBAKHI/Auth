using System.Globalization;
using System.Resources;
using Auth.Application.Behaviors;
using Auth.Application.Features.Notifications.UpdateNotificationTemplateDraft;
using Auth.Domain.Errors;
using Auth_Localization.Resources.Errors;
using ErrorOr;
using FluentValidation;
using FluentValidation.Validators;
using Xunit;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// The catalog and the published list (<c>docs/api/error-codes.json</c>) describe the same codes
/// (ADR 0001): whatever a handler or a validator can return is published, and whatever is
/// published as a catalog code has exactly one member that builds it.
/// </summary>
public class ErrorCatalogContractTests
{
    /// <summary>
    /// Codes that a catalog method builds only on a branch the placeholder arguments of
    /// <see cref="ErrorCatalog"/> do not take (a null date).
    /// </summary>
    private static readonly string[] ConditionallyBuiltCodes =
    [
        "Session.MaxSessionsReached",
    ];

    /// <summary>
    /// Validators whose rules include a child validator (<c>ChildRules</c> / <c>SetValidator</c>).
    /// The descriptor walk cannot see inside a child validator, so each one listed here has a
    /// behavioural test below that runs its child rules.
    /// </summary>
    private static readonly Type[] ValidatorsWithChildRules =
    [
        typeof(UpdateNotificationTemplateDraftCommandValidator),
    ];

    [Fact]
    public void EveryCatalogCode_IsPublishedAsACatalogCode()
    {
        var unpublished = ErrorCatalog.Members()
            .Where(member => !PublishedErrorCodes.All.TryGetValue(member.Error.Code, out var published)
                || published.Source != "catalog")
            .Select(member => $"{member.Member} -> {member.Error.Code}")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unpublished);
    }

    [Fact]
    public void NoCatalogCode_UsesTheLibraryDefaultNamespace()
    {
        var general = ErrorCatalog.Members()
            .Where(member => member.Error.Code.StartsWith("General.", StringComparison.Ordinal))
            .Select(member => $"{member.Member} -> {member.Error.Code}")
            .ToList();

        Assert.Empty(general);
    }

    [Fact]
    public void EveryPublishedCatalogCode_IsBuiltByACatalogMember()
    {
        var built = ErrorCatalog.Members()
            .Select(member => member.Error.Code)
            .Concat(ConditionallyBuiltCodes)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = PublishedErrorCodes.All.Values
            .Where(code => code.Source == "catalog" && !built.Contains(code.Code))
            .Select(code => code.Code)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void EveryCode_IsBuiltByOneCatalogMember()
    {
        var shared = ErrorCatalog.Members()
            .GroupBy(member => member.Error.Code, StringComparer.Ordinal)
            .Where(group => group.Select(member => member.Member).Distinct().Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(member => member.Member))}")
            .ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void EveryDomainErrorsResourceKey_IsAPublishedCode()
    {
        var resources = new ResourceManager(typeof(DomainErrors).FullName!, typeof(DomainErrors).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;

        var orphans = resources.Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Where(key => !PublishedErrorCodes.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryValidationRule_HasAPublishedValidationCode()
    {
        var catalog = CatalogByCode();
        var components = ValidatorRuleComponents().ToList();

        var offending = components
            .Where(component => component.Validator is not IChildValidatorAdaptor)
            .Where(component => !IsPublishedValidationCode(component.ErrorCode, catalog))
            .Select(component =>
                $"{component.ValidatorType.Name}.{component.PropertyName} ({component.Validator.Name}) -> '{component.ErrorCode}'")
            .ToList();

        Assert.True(components.Count > 300, $"The walk found only {components.Count} rule components.");
        Assert.Empty(offending);
    }

    [Fact]
    public void ChildValidators_AppearOnlyWhereABehaviouralTestCoversThem()
    {
        var withChildRules = ValidatorRuleComponents()
            .Where(component => component.Validator is IChildValidatorAdaptor)
            .Select(component => component.ValidatorType)
            .Distinct()
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ValidatorsWithChildRules.OrderBy(type => type.Name, StringComparer.Ordinal), withChildRules);
    }

    [Fact]
    public void UpdateNotificationTemplateDraft_ChildRules_ReturnPublishedValidationCodes()
    {
        var catalog = CatalogByCode();
        var tooLongBody = new string('x', 512_001);
        var command = new UpdateNotificationTemplateDraftCommand(
            Guid.NewGuid(),
            [
                new DraftTranslationInput(string.Empty, string.Empty, string.Empty, tooLongBody),
                new DraftTranslationInput("xx", new string('s', 501), tooLongBody),
            ]);

        var failures = new UpdateNotificationTemplateDraftCommandValidator().Validate(command).Errors;

        Assert.All(failures, failure => Assert.True(
            IsPublishedValidationCode(failure.ErrorCode, catalog),
            $"{failure.PropertyName} -> '{failure.ErrorCode}'"));
        Assert.Equal(
            new[]
            {
                NotificationErrors.BodyHtmlRequired.Code,
                NotificationErrors.BodyHtmlTooLong.Code,
                NotificationErrors.BodyTextTooLong.Code,
                NotificationErrors.SubjectRequired.Code,
                NotificationErrors.SubjectTooLong.Code,
                NotificationErrors.TranslationLanguageNotSupported.Code,
                NotificationErrors.TranslationLanguageRequired.Code,
            },
            failures.Select(failure => failure.ErrorCode).Distinct().Order(StringComparer.Ordinal));
    }

    private static Dictionary<string, Error> CatalogByCode() =>
        ErrorCatalog.Members()
            .GroupBy(member => member.Error.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Error, StringComparer.Ordinal);

    private static bool IsPublishedValidationCode(string? code, IReadOnlyDictionary<string, Error> catalog) =>
        !string.IsNullOrEmpty(code)
        && PublishedErrorCodes.Contains(code)
        && catalog.TryGetValue(code, out var error)
        && error.Type == ErrorType.Validation;

    private static IEnumerable<RuleComponent> ValidatorRuleComponents()
    {
        var validatorTypes = typeof(ValidationBehavior<,>).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                && typeof(IValidator).IsAssignableFrom(type));

        foreach (var type in validatorTypes)
        {
            var validator = (IValidator)Activator.CreateInstance(type)!;
            foreach (var rule in validator.CreateDescriptor().Rules)
            {
                foreach (var component in rule.Components)
                {
                    yield return new RuleComponent(type, rule.PropertyName, component.Validator, component.ErrorCode);
                }
            }
        }
    }

    private sealed record RuleComponent(
        Type ValidatorType,
        string PropertyName,
        IPropertyValidator Validator,
        string? ErrorCode);
}
