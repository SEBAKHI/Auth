using System.Reflection;
using Auth.Application.DTOs;
using Auth_API.Modules.OrganizationManagement.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Auth_API.Tests.OrganizationManagement;

/// <summary>
/// OI-195: no invitation response carries anything named like a token, at any depth.
/// </summary>
/// <remarks>
/// <see cref="InvitationResponseGuardTests"/> holds three DTOs by name and reads their own
/// properties. A response that nests a type (<see cref="InvitationAcceptResultDto"/> carries
/// <see cref="InvitationApplicationDto"/>; the list now wraps its rows in
/// <see cref="PagedOrganizationInvitationsDto"/>) or a new endpoint with a new response type
/// is outside that reach. This walk starts from the endpoints instead: every action on an
/// invitation route, every success type it declares, and every type reachable from those
/// through properties, collection elements and generic arguments.
/// </remarks>
public class InvitationResponseTokenWalkTests
{
    [Fact]
    public void NoTypeReachableFromAnInvitationResponse_HasAPropertyNamedLikeAToken()
    {
        var offenders = new List<string>();

        foreach (var (root, path, property) in Walk(InvitationResponseTypes()))
        {
            if (property.Name.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add($"{root.Name}: {path}");
            }
        }

        offenders.Should().BeEmpty(
            "redeeming an invitation token confirms an email address with no further proof, so no "
            + "response under an invitation route may carry one, however deeply nested");
    }

    [Fact]
    public void TheWalk_ReachesTheNestedAndPagedTypes()
    {
        // A walk that found nothing would pass the test above for any DTO. This is its floor:
        // the types the guard was written for must be among the ones it actually read.
        var reached = Walk(InvitationResponseTypes())
            .Select(step => step.Property.DeclaringType)
            .ToHashSet();

        reached.Should().Contain([
            typeof(InvitationApplicationDto),
            typeof(RegisterWithInvitationResponse),
            typeof(PagedOrganizationInvitationsDto),
            typeof(OrganizationInvitationDto),
            typeof(InvitationPreviewDto),
            typeof(InvitationAcceptResultDto),
        ]);
    }

    [Fact]
    public void TheEndpointsFound_AreTheInvitationRoutes()
    {
        InvitationActions().Select(action => action.Name).Should().Contain([
            nameof(OrganizationsController.GetPendingInvitations),
            nameof(OrganizationsController.InviteMember),
            nameof(OrganizationsController.ResendInvitation),
            nameof(InvitationsController.GetInvitationByToken),
            nameof(InvitationsController.RegisterWithInvitation),
            nameof(InvitationsController.AcceptInvitation),
        ]);
    }

    #region Walk

    /// <summary>Actions whose route mentions invitations, on any controller of the API.</summary>
    private static IEnumerable<MethodInfo> InvitationActions() =>
        typeof(OrganizationsController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(action => RouteOf(controller, action).Contains("invitation", StringComparison.OrdinalIgnoreCase)));

    private static string RouteOf(Type controller, MethodInfo action)
    {
        var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        if (prefix.Contains("[controller]", StringComparison.Ordinal))
        {
            prefix = prefix.Replace("[controller]", controller.Name[..^"Controller".Length], StringComparison.Ordinal);
        }

        var templates = action.GetCustomAttributes<HttpMethodAttribute>().Select(http => http.Template ?? string.Empty);
        return string.Join("|", templates.Select(template => $"{prefix}/{template}"));
    }

    /// <summary>The 2xx types the invitation actions declare.</summary>
    private static IEnumerable<Type> InvitationResponseTypes() =>
        InvitationActions()
            .SelectMany(action => action.GetCustomAttributes<ProducesResponseTypeAttribute>())
            .Where(produces => produces.StatusCode is >= 200 and < 300 && produces.Type != typeof(void))
            .Select(produces => produces.Type)
            .Distinct();

    private static IEnumerable<(Type Root, string Path, PropertyInfo Property)> Walk(IEnumerable<Type> roots)
    {
        foreach (var root in roots)
        {
            var seen = new HashSet<Type>();
            var pending = new Queue<(Type Type, string Path)>();
            pending.Enqueue((root, root.Name));

            while (pending.TryDequeue(out var current))
            {
                foreach (var type in Unwrap(current.Type))
                {
                    if (!IsOurs(type) || !seen.Add(type))
                    {
                        continue;
                    }

                    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var path = $"{current.Path}.{property.Name}";
                        yield return (root, path, property);
                        pending.Enqueue((property.PropertyType, path));
                    }
                }
            }
        }
    }

    /// <summary>The type itself, its element type, and its generic arguments, recursively.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        yield return underlying;

        if (underlying.IsArray)
        {
            foreach (var inner in Unwrap(underlying.GetElementType()!))
            {
                yield return inner;
            }
        }

        foreach (var argument in underlying.IsGenericType ? underlying.GetGenericArguments() : [])
        {
            foreach (var inner in Unwrap(argument))
            {
                yield return inner;
            }
        }
    }

    /// <summary>The API's own types; framework types (string, DateTime, collections) are not walked into.</summary>
    private static bool IsOurs(Type type) =>
        type.Namespace?.StartsWith("Auth", StringComparison.Ordinal) == true && !type.IsEnum;

    #endregion
}
