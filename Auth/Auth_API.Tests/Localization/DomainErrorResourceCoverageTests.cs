using System.Globalization;
using System.Reflection;
using System.Resources;
using Auth.Domain.Errors;
using Auth_Localization.Resources.Errors;
using ErrorOr;
using Xunit;

namespace Auth_API.Tests.Localization;

/// <summary>
/// Guards that every domain error code has an entry in DomainErrors.resx, so no
/// error falls back to its hardcoded English description on localized requests.
/// Errors constructed inline inside handlers cannot be discovered by reflection;
/// register their codes in <see cref="HandlerInlineCodes"/> when adding one.
/// </summary>
public class DomainErrorResourceCoverageTests
{
    private static readonly string[] HandlerInlineCodes =
    [
        "Organization.AdminRoleNotFound",
        "Organization.OwnerRoleNotFound",
        "Role.CannotUpdateSystemRole",
        "User.RoleAlreadyAssigned",
    ];

    [Fact]
    public void EveryDomainErrorCode_HasDomainErrorsResourceEntry()
    {
        var resourceManager = new ResourceManager(
            typeof(DomainErrors).FullName!,
            typeof(DomainErrors).Assembly);

        var missing = DiscoverErrorCodes()
            .Concat(HandlerInlineCodes)
            .Distinct()
            .Where(code => resourceManager.GetString(code, CultureInfo.InvariantCulture) is null)
            .OrderBy(code => code)
            .ToList();

        Assert.Empty(missing);
    }

    private static IEnumerable<string> DiscoverErrorCodes()
    {
        var errorClasses = typeof(UserErrors).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: true, IsSealed: true }
                && t.Namespace == typeof(UserErrors).Namespace);

        foreach (var type in errorClasses)
        {
            var properties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(Error));
            foreach (var property in properties)
            {
                yield return ((Error)property.GetValue(null)!).Code;
            }

            var methods = type
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(Error) && !m.IsSpecialName);
            foreach (var method in methods)
            {
                var args = method.GetParameters()
                    .Select(p => DummyArgument(p.ParameterType))
                    .ToArray();
                yield return ((Error)method.Invoke(null, args)!).Code;
            }
        }
    }

    private static object? DummyArgument(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return "sample";
        if (underlying == typeof(Guid)) return Guid.NewGuid();
        if (underlying == typeof(DateTime)) return DateTime.UtcNow;
        if (underlying == typeof(TimeSpan)) return TimeSpan.FromMinutes(1);
        if (underlying.IsEnum) return Enum.GetValues(underlying).GetValue(0);

        return underlying.IsValueType ? Activator.CreateInstance(underlying) : null;
    }
}
