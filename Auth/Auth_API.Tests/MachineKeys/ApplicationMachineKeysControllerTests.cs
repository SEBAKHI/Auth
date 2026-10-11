using System.Reflection;
using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Authorization;
using Auth_API.Modules.ApplicationManagement.Controllers;
using Auth_API.Modules.AuditLog.EventHandlers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.MachineKeys;

/// <summary>
/// The machine-key endpoints' authorization and routes (OI-128, D2, T5), read by reflection as the
/// pipeline reads them, and the two audit handlers.
/// </summary>
public class ApplicationMachineKeysControllerTests
{
    private static MethodInfo Action(string name) =>
        typeof(ApplicationMachineKeysController).GetMethod(name)
        ?? throw new InvalidOperationException($"{name} not found");

    [Theory]
    [InlineData(nameof(ApplicationMachineKeysController.GetMachineKeys), PermissionCodes.ApiKeys.Read)]
    [InlineData(nameof(ApplicationMachineKeysController.CreateMachineKey), PermissionCodes.ApiKeys.Create)]
    [InlineData(nameof(ApplicationMachineKeysController.RevokeMachineKey), PermissionCodes.ApiKeys.Revoke)]
    public void EachAction_DemandsItsApiKeyPermission(string action, string permission)
    {
        Action(action).GetCustomAttributes<RequirePermissionAttribute>()
            .Select(attribute => attribute.Permission).Should().Equal(permission);
    }

    [Fact]
    public void EveryPublicAction_IsCovered_AndTheControllerNeedsAPlatformUser()
    {
        typeof(ApplicationMachineKeysController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Should().BeEquivalentTo(
                nameof(ApplicationMachineKeysController.GetMachineKeys),
                nameof(ApplicationMachineKeysController.CreateMachineKey),
                nameof(ApplicationMachineKeysController.RevokeMachineKey));

        var authorize = typeof(ApplicationMachineKeysController).GetCustomAttribute<AuthorizeAttribute>();
        authorize.Should().NotBeNull();
        authorize!.AuthenticationSchemes.Should().BeNull("the default, platform, scheme: no application or machine token manages keys");
        typeof(ApplicationMachineKeysController).GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }

    [Fact]
    public void TheRoutes_AreUnderTheApplication()
    {
        typeof(ApplicationMachineKeysController).GetCustomAttribute<RouteAttribute>()!.Template
            .Should().Be("api/v{version:apiVersion}/applications/{applicationId:guid}/machine-keys");
        Action(nameof(ApplicationMachineKeysController.RevokeMachineKey)).GetCustomAttribute<HttpPostAttribute>()!.Template
            .Should().Be("{keyId:guid}/revoke");
    }

    // --- the audit handlers ---

    [Fact]
    public async Task CreatedAuditHandler_WritesTheCatalogueAction_WithSerializedDetails()
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var created = new MachineKeyCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "kid\"}, \"x\":1", true, Guid.NewGuid(), DateTimeOffset.UtcNow);

        await new MachineKeyCreatedAuditEventHandler(audit.Object, NullLogger<MachineKeyCreatedAuditEventHandler>.Instance)
            .Handle(created, CancellationToken.None);

        written!.Action.Should().Be(AuditActions.MachineKeyCreated);
        written.ActionType.Should().Be(AuditActionTypes.ApiKeyManagement);
        written.EntityId.Should().Be(created.MachineKeyId);
        written.PerformedBy.Should().Be(created.TriggeredBy);
        written.ApplicationId.Should().Be(created.ApplicationId);
        using var details = JsonDocument.Parse(written.AdditionalData!);
        details.RootElement.GetProperty("kid").GetString().Should().Be(created.Kid, "a quote in the value cannot break the JSON");
        details.RootElement.GetProperty("generated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RevokedAuditHandler_WritesTheCatalogueAction()
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var revoked = new MachineKeyRevokedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "kid-1", Guid.NewGuid(), DateTimeOffset.UtcNow);

        await new MachineKeyRevokedAuditEventHandler(audit.Object, NullLogger<MachineKeyRevokedAuditEventHandler>.Instance)
            .Handle(revoked, CancellationToken.None);

        written!.Action.Should().Be(AuditActions.MachineKeyRevoked);
        written.EntityId.Should().Be(revoked.MachineKeyId);
        JsonDocument.Parse(written.AdditionalData!).RootElement.GetProperty("kid").GetString().Should().Be("kid-1");
    }

    [Fact]
    public async Task TheAuditHandlers_NeverFailTheCommittedChange()
    {
        var audit = new Mock<IAuditLogRepository>();
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var create = () => new MachineKeyCreatedAuditEventHandler(audit.Object, NullLogger<MachineKeyCreatedAuditEventHandler>.Instance)
            .Handle(new MachineKeyCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "k", false, null, DateTimeOffset.UtcNow), CancellationToken.None);
        var revoke = () => new MachineKeyRevokedAuditEventHandler(audit.Object, NullLogger<MachineKeyRevokedAuditEventHandler>.Instance)
            .Handle(new MachineKeyRevokedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "k", null, DateTimeOffset.UtcNow), CancellationToken.None);

        await create.Should().NotThrowAsync();
        await revoke.Should().NotThrowAsync();
    }
}
