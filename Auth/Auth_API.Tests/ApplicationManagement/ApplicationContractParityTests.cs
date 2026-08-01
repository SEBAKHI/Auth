using System.Reflection;
using Auth.Application.DTOs;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth_API.Modules.ApplicationManagement.Contracts;

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// Guards the create/update symmetry for applications.
///
/// The two paths were written separately and drifted: redirect URIs were
/// settable only on update, so a newly created OAuth client could not complete
/// a single authorization request until someone reopened it and edited it. The
/// console mirrors these contracts field for field, so a gap here shows up as a
/// setting that exists in one dialog and is missing from the other.
///
/// Everything an application can be configured with must therefore be settable
/// at creation too. `Code` is the sole exception: it is immutable, so it exists
/// on create only.
/// </summary>
public class ApplicationContractParityTests
{
    private const string ImmutableAfterCreation = nameof(CreateApplicationRequest.Code);

    [Fact]
    public void CreateAndUpdateRequests_ExposeTheSameConfigurationSurface()
    {
        var create = SettableNames(typeof(CreateApplicationRequest));
        var update = SettableNames(typeof(UpdateApplicationRequest));

        create.Except([ImmutableAfterCreation]).Should().BeEquivalentTo(
            update,
            "every application setting must be configurable at creation, not only afterwards");
    }

    [Fact]
    public void CreateAndUpdateCommands_ExposeTheSameConfigurationSurface()
    {
        // The commands carry one extra audit member each (CreatedBy/ModifiedBy)
        // and the update carries the route Id; the rest must match.
        var create = SettableNames(typeof(CreateApplicationCommand))
            .Except([ImmutableAfterCreation, nameof(CreateApplicationCommand.CreatedBy)]);
        var update = SettableNames(typeof(UpdateApplicationCommand))
            .Except([nameof(UpdateApplicationCommand.Id), nameof(UpdateApplicationCommand.ModifiedBy)]);

        create.Should().BeEquivalentTo(update);
    }

    [Fact]
    public void ApplicationDto_ReportsEverySettingBothPathsAccept()
    {
        // A write-only setting is invisible to the console: the edit dialog
        // seeds itself from this DTO, so anything missing here is submitted back
        // empty and silently wipes the stored value.
        var settings = SettableNames(typeof(UpdateApplicationRequest));
        var reported = SettableNames(typeof(ApplicationDto));

        reported.Should().Contain(settings);
    }

    private static IEnumerable<string> SettableNames(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.Name != "EqualityContract")
            .Select(property => property.Name)
            .ToList();
    }
}
