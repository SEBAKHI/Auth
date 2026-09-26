using System.Reflection;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// Every member of the <c>{Concept}Errors</c> catalog in Auth.Domain/Errors: <c>static readonly</c>
/// fields, properties, and methods, the last invoked with placeholder arguments. One walk shared by
/// the resource-coverage and the contract tests, so both see the same members.
/// </summary>
internal static class ErrorCatalog
{
    public static IEnumerable<(string Member, Error Error)> Members()
    {
        var catalogClasses = typeof(UserErrors).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: true, IsSealed: true }
                && t.Namespace == typeof(UserErrors).Namespace
                && t.Name.EndsWith("Errors", StringComparison.Ordinal));

        foreach (var type in catalogClasses)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(Error)))
            {
                yield return ($"{type.Name}.{field.Name}", (Error)field.GetValue(null)!);
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(Error)))
            {
                yield return ($"{type.Name}.{property.Name}", (Error)property.GetValue(null)!);
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(Error) && !m.IsSpecialName))
            {
                var args = method.GetParameters()
                    .Select(p => DummyArgument(p.ParameterType))
                    .ToArray();
                yield return ($"{type.Name}.{method.Name}", (Error)method.Invoke(null, args)!);
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
