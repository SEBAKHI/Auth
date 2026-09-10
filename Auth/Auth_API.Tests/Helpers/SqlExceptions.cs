using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// Builds a <see cref="SqlException"/> carrying a given server error number.
/// </summary>
/// <remarks>
/// SqlClient exposes no public way to construct one, so the tests that assert
/// how the middleware maps server errors (547 for a foreign key, 2601/2627 for
/// a unique violation) reach the internal factory by reflection. Every member
/// is looked up by shape rather than by exact signature, so a SqlClient upgrade
/// that adds a parameter fails here with a message naming the member, not with
/// a null-reference somewhere in a test.
/// </remarks>
internal static class SqlExceptions
{
    public static SqlException WithNumber(int number, string message = "simulated server error")
    {
        var collectionType = typeof(SqlErrorCollection);
        var collection = Activator.CreateInstance(collectionType, nonPublic: true)
            ?? throw new InvalidOperationException("SqlErrorCollection has no parameterless constructor.");

        var errorType = typeof(SqlError);
        var constructor = errorType
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("SqlError has no non-public constructor.");

        var numberAssigned = false;
        var arguments = constructor.GetParameters().Select(parameter =>
        {
            var type = parameter.ParameterType;
            if (type == typeof(int) && !numberAssigned)
            {
                numberAssigned = true;
                return (object?)number;
            }

            if (type == typeof(string))
            {
                return parameter.Name is "errorMessage" ? message : string.Empty;
            }

            return parameter.HasDefaultValue
                ? parameter.DefaultValue
                : type.IsValueType ? Activator.CreateInstance(type) : null;
        }).ToArray();

        var error = constructor.Invoke(arguments);

        var add = collectionType.GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance, [errorType])
            ?? throw new InvalidOperationException("SqlErrorCollection.Add(SqlError) was not found.");
        add.Invoke(collection, [error]);

        var create = typeof(SqlException)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "CreateException"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == collectionType
                && m.GetParameters()[1].ParameterType == typeof(string))
            ?? throw new InvalidOperationException("SqlException.CreateException(SqlErrorCollection, string) was not found.");

        var exception = (SqlException)create.Invoke(null, [collection, "16.0"])!;
        exception.Number.Should().Be(number, "the reflection-built error must carry the requested number");
        return exception;
    }
}
