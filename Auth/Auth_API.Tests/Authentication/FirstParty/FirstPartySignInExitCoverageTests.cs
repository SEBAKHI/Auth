using System.Reflection;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Features.Authentication.CompleteRegistration;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.VerifyEmail;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth.Application.Interfaces;
using Auth_API.Common.FirstParty;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Infrastructure;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// Every way into a session goes through ONE place that writes its cookies
/// (<see cref="FirstPartySessionResultFilter"/>). A sign-in exit that bypasses it
/// hands the first-party apps a refresh token in a script-readable body — exactly
/// what the two account-recovery actions did with the SSO cookie before, when each
/// action wrote its own. Two legs, because an exit can be born on either side:
/// <list type="number">
/// <item>an ACTION that returns a session (reflection over the controllers);</item>
/// <item>a COMMAND that produces one (reflection over Auth.Application), mapped
/// explicitly to the action that serves it.</item>
/// </list>
/// A passkey or email-code sign-in added later (S20, S24) lands here first.
/// </summary>
public class FirstPartySignInExitCoverageTests
{
    private static readonly Type[] SessionTypes = [typeof(LoginResponse), typeof(TokenResponse)];

    /// <summary>
    /// Every command that produces a session, and the action that serves it. A new
    /// command must be added here, pointing at an action that carries the attribute.
    /// </summary>
    private static readonly Dictionary<Type, (Type Controller, string Action)> CommandToAction = new()
    {
        [typeof(LoginCommand)] = (typeof(AuthController), nameof(AuthController.Login)),
        [typeof(ExternalLoginCommand)] = (typeof(AuthController), nameof(AuthController.ExternalLogin)),
        [typeof(CompleteRegistrationCommand)] = (typeof(AuthController), nameof(AuthController.CompleteRegistration)),
        [typeof(VerifyEmailCommand)] = (typeof(AuthController), nameof(AuthController.VerifyEmail)),
        [typeof(RecoverAccountCommand)] = (typeof(AuthController), nameof(AuthController.RecoverAccount)),
        [typeof(RecoverAccountExternalCommand)] = (typeof(AuthController), nameof(AuthController.RecoverAccountExternal)),
        [typeof(VerifyTwoFactorLoginCommand)] = (typeof(TwoFactorController), nameof(TwoFactorController.Verify)),
        [typeof(RefreshTokenCommand)] = (typeof(AuthController), nameof(AuthController.RefreshToken)),
    };

    private static IEnumerable<MethodInfo> Actions() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    private static bool ReturnsASession(MethodInfo action) =>
        action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(produces => produces.StatusCode == StatusCodes.Status200OK && SessionTypes.Contains(produces.Type));

    [Fact]
    public void Leg1_EveryActionThatReturnsASession_CarriesIssuesFirstPartySession()
    {
        var exits = Actions().Where(ReturnsASession).ToList();

        // A floor, so the guard cannot pass because it stopped seeing anything.
        exits.Should().HaveCountGreaterThanOrEqualTo(8);

        var bypassing = exits
            .Where(action => action.GetCustomAttribute<IssuesFirstPartySessionAttribute>() is null)
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")
            .ToList();

        bypassing.Should().BeEmpty(
            "every action that answers with a LoginResponse or TokenResponse must deliver it through " +
            "[IssuesFirstPartySession], or its refresh token reaches page scripts");
    }

    private static bool IsSessionResult(Type result) =>
        SessionTypes.Contains(result) ||
        result.GetProperties().Any(property => property.PropertyType == typeof(LoginResponse));

    private static IEnumerable<Type> SessionCommands() =>
        typeof(ILoginResponseBuilder).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface)
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(IRequest<>) &&
                contract.GetGenericArguments()[0] is { IsGenericType: true } result &&
                result.GetGenericTypeDefinition() == typeof(ErrorOr<>) &&
                IsSessionResult(result.GetGenericArguments()[0])));

    [Fact]
    public void Leg2_EveryCommandThatProducesASession_IsMappedToAnActionThatCarriesTheAttribute()
    {
        var commands = SessionCommands().ToList();

        // A floor, so the guard cannot pass because reflection stopped finding anything.
        commands.Should().HaveCountGreaterThanOrEqualTo(8);

        // Named first: a new command fails here with its own name in the message.
        foreach (var command in commands)
        {
            CommandToAction.Should().ContainKey(command,
                $"{command.Name} produces a session: map it to the action that serves it, and give that " +
                "action [IssuesFirstPartySession]");

            var (controller, actionName) = CommandToAction[command];
            var action = controller.GetMethod(actionName);
            action.Should().NotBeNull($"{controller.Name}.{actionName}");
            action!.GetCustomAttribute<IssuesFirstPartySessionAttribute>().Should().NotBeNull(
                $"{controller.Name}.{actionName} serves {command.Name}");
        }

        // And no stale entry: a command removed from the code must leave the map too.
        CommandToAction.Keys.Should().BeEquivalentTo(commands);
    }

    [Fact]
    public void NoModule_WritesTheSsoCookieItself()
    {
        var modules = Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API", "Modules");
        var files = Directory.EnumerateFiles(modules, "*.cs", SearchOption.AllDirectories).ToList();

        files.Should().NotBeEmpty();
        files.Where(file => File.ReadAllText(file).Contains("IdpSessionCookie.Apply(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Should().BeEmpty("auth_idp is written only by FirstPartySessionResultFilter");
    }
}
