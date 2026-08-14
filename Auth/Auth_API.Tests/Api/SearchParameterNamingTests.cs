using System.Reflection;
using Auth_API.Modules.Media.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Api;

/// <summary>
/// Every endpoint that accepts a free-text search must spell the parameter the
/// same way.
///
/// Ten endpoints accept one, and they used to be split: six said <c>search</c>,
/// four said <c>searchTerm</c>, and OrganizationsController managed to use both
/// in the same file. Nothing enforced a choice, so each new caller guessed.
///
/// Guessing wrong fails SILENTLY. ASP.NET ignores an unrecognized query
/// parameter, so the server returns the unfiltered first page: no 400, no log
/// line, just a screen that looks like search is broken. That is exactly how a
/// wrong parameter name survived review in the trial-user picker until a human
/// noticed the list was not narrowing.
///
/// <c>searchTerm</c> is the name, because it is what the MediatR queries and the
/// repository interfaces already used nine times out of ten — so the name is now
/// identical from the URL all the way down to the SQL parameter.
/// </summary>
public class SearchParameterNamingTests
{
    private const string RequiredName = "searchTerm";

    /// <summary>
    /// Parameter names that read as a free-text search but are something else,
    /// so a rename would be wrong.
    /// </summary>
    private static readonly HashSet<string> NotAFreeTextSearch = new(StringComparer.Ordinal)
    {
        // Nothing today. An entry here is a decision that costs a deliberate
        // edit rather than a silently passing test.
    };

    [Fact]
    public void EveryFreeTextSearchParameter_IsCalledSearchTerm()
    {
        var offenders = new List<string>();

        foreach (var (controller, action, parameter) in QueryStringParameters())
        {
            if (NotAFreeTextSearch.Contains(parameter.Name!))
            {
                continue;
            }

            // Only string parameters whose name is search-ish are candidates;
            // this must not catch `sortBy`, `isActive` and friends.
            var looksLikeSearch =
                parameter.ParameterType == typeof(string) &&
                parameter.Name!.Contains("search", StringComparison.OrdinalIgnoreCase);

            if (looksLikeSearch && parameter.Name != RequiredName)
            {
                offenders.Add($"{controller}.{action}({parameter.Name})");
            }
        }

        offenders.Should().BeEmpty(
            $"every search parameter must be named '{RequiredName}'; a caller that " +
            "guesses the other spelling gets an unfiltered list back with no error " +
            "to tell them why");
    }

    [Fact]
    public void AtLeastOneSearchParameterExists_SoThisGuardCannotPassVacuously()
    {
        var found = QueryStringParameters()
            .Count(p => p.Parameter.ParameterType == typeof(string)
                        && p.Parameter.Name!.Contains("search", StringComparison.OrdinalIgnoreCase));

        found.Should().BeGreaterThan(5,
            "the API has several searchable lists; finding almost none means this " +
            "test stopped looking at the right place and is guarding nothing");
    }

    private static IEnumerable<(string Controller, string Action, ParameterInfo Parameter)>
        QueryStringParameters()
    {
        var controllers = typeof(ImagesController).Assembly
            .GetTypes()
            .Where(type => type.IsClass
                && !type.IsAbstract
                && typeof(ControllerBase).IsAssignableFrom(type));

        foreach (var controller in controllers)
        {
            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.DeclaringType == controller);

            foreach (var action in actions)
            {
                foreach (var parameter in action.GetParameters())
                {
                    // [FromQuery] is explicit on every list endpoint here; an
                    // unattributed simple parameter also binds from the query
                    // string, so both count.
                    var isFromQuery = parameter.GetCustomAttribute<FromQueryAttribute>() is not null;
                    var isFromElsewhere =
                        parameter.GetCustomAttribute<FromBodyAttribute>() is not null ||
                        parameter.GetCustomAttribute<FromRouteAttribute>() is not null ||
                        parameter.GetCustomAttribute<FromHeaderAttribute>() is not null ||
                        parameter.GetCustomAttribute<FromFormAttribute>() is not null ||
                        parameter.GetCustomAttribute<FromServicesAttribute>() is not null;

                    if (isFromQuery || !isFromElsewhere)
                    {
                        yield return (controller.Name, action.Name, parameter);
                    }
                }
            }
        }
    }
}
