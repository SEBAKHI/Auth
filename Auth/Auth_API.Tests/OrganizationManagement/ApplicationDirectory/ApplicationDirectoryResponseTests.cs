using System.Reflection;
using System.Text.Json.Serialization;
using Auth.Application.DTOs;
using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth_API.Modules.OrganizationManagement.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.OrganizationManagement.ApplicationDirectory;

/// <summary>
/// What the application directory (OI-194) can hand an application, read from the endpoints:
/// every 2xx type its three actions declare, walked through properties, collection elements and
/// generic arguments by the same walk that guards the invitation routes (OI-193/OI-195).
/// </summary>
public class ApplicationDirectoryResponseTests
{
    private static IEnumerable<Type> DirectoryResponseTypes() =>
        typeof(ApplicationOrganizationsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetCustomAttributes<ProducesResponseTypeAttribute>())
            .Where(produces => produces.StatusCode is >= 200 and < 300 && produces.Type != typeof(void))
            .Select(produces => produces.Type)
            .Distinct();

    [Fact]
    public void NoTypeReachableFromADirectoryResponse_HasAPropertyNamedLikeAToken()
    {
        var offenders = InvitationResponseTokenWalkTests.Walk(DirectoryResponseTypes())
            .Where(step => step.Property.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || step.Property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    .Contains("token", StringComparison.OrdinalIgnoreCase) == true)
            .Select(step => $"{step.Root.Name}: {step.Path}")
            .ToList();

        offenders.Should().BeEmpty("an application never receives an invitation token, its hash, or anything named like one");
    }

    [Fact]
    public void TheWalk_ReachesEveryDirectoryRow()
    {
        // Its floor: a walk that read nothing would pass the test above for any type.
        var reached = InvitationResponseTokenWalkTests.Walk(DirectoryResponseTypes())
            .Select(step => step.Property.DeclaringType)
            .ToHashSet();

        reached.Should().Contain([
            typeof(ApplicationOrganizationResponse),
            typeof(ApplicationOrganizationMemberResponse),
            typeof(ApplicationOrganizationInvitationResponse),
            typeof(ApplicationDirectoryPage<ApplicationOrganizationMemberResponse>),
            typeof(ApplicationDirectoryPage<ApplicationOrganizationInvitationResponse>),
        ]);
    }

    [Fact]
    public void NoDirectoryResponse_IsAConsoleType_OrNamesTheInviter()
    {
        var reached = InvitationResponseTokenWalkTests.Walk(DirectoryResponseTypes()).ToList();

        reached.Select(step => step.Property.DeclaringType!.Namespace)
            .Should().OnlyContain(ns => ns == typeof(ApplicationOrganizationResponse).Namespace,
                "the console's DTOs ({0}) grow fields for the console, which must never reach an application",
                typeof(OrganizationInvitationDto).Namespace);
        reached.Select(step => step.Property.Name)
            .Should().NotContain(name => name.StartsWith("InvitedBy", StringComparison.Ordinal) || name == "IsActive");
    }
}
