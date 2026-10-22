using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The secret-management admin API switch (AM-S11-1): one line each time it is turned on
/// or off, a Warning while it is on. The switch is read per request (an
/// <see cref="IOptionsSnapshot{TOptions}"/>), so turning it on or off is a settings-file
/// edit that takes effect on the next request; this line is the record of that edit.
/// </summary>
/// <remarks>
/// Read here the same way the gate reads it: through a snapshot in a scope of its own,
/// never a monitor, whose value can stay on after a reload of a settings file that failed
/// to parse.
/// </remarks>
public sealed class AdminApiPostureContributor : ISecurityPostureContributor
{
    private const string OnTemplate =
        "Secret management admin API is ON (SecretManagement:EnableAdminApi = true): every key operation is reachable. Turn it off when the key work is done";
    private const string OffTemplate =
        "Secret management admin API is off (SecretManagement:EnableAdminApi = false)";

    private readonly IServiceScopeFactory _scopeFactory;

    public AdminApiPostureContributor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Name => "secrets.admin-api";

    /// <inheritdoc />
    public Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var enabled = scope.ServiceProvider
            .GetRequiredService<IOptionsSnapshot<SecretManagementSettings>>().Value.EnableAdminApi;

        return Task.FromResult(enabled
            ? new SecurityPosture(
                SecurityPostureLevel.Weak,
                ["secrets.admin-api"],
                [new SecurityPostureLine(LogLevel.Warning, OnTemplate, [])],
                "on")
            : new SecurityPosture(
                SecurityPostureLevel.Ok,
                [],
                [new SecurityPostureLine(LogLevel.Information, OffTemplate, [])],
                "off"));
    }
}
