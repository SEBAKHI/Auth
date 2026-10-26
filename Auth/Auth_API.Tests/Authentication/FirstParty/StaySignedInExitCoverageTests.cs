using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Features.AccountDeletion.RecoverAccountWithEmailCode;
using Auth.Application.Features.Authentication.CompleteRegistration;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.SignInWithEmailCode;
using Auth.Application.Features.Authentication.VerifyEmail;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth_API.Common.FirstParty;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Match = System.Text.RegularExpressions.Match;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// OI-154: every action that hands a browser a session says how long its cookies
/// live, under the default <c>PersonChooses</c>. Three carry the person's "Stay
/// signed in" choice; every other sign-in mints a browser session even when a
/// client sends the field; the refresh follows the IdP session the request
/// presents. A new exit fails here until it is classified, and the builder's
/// default (false) means one that is not classified can only fail closed. The
/// administrator's other two values (OI-165) apply in the builder to every exit
/// alike: <c>LoginResponseBuilderStaySignedInTests</c>, <c>StaySignedInModeCookieTests</c>.
/// </summary>
public class StaySignedInExitCoverageTests
{
    private enum Lifetime
    {
        /// <summary>The request body's <c>staySignedIn</c>, carried to the command.</summary>
        PersonsChoice,

        /// <summary>Always a browser session: the request has no such field.</summary>
        BrowserSession,

        /// <summary>The refresh: the presented IdP session decides (FirstPartySessionResultFilter).</summary>
        PresentedSession,
    }

    private static readonly Dictionary<string, (Lifetime Lifetime, Type? Command)> Exits = new()
    {
        [$"{nameof(AuthController)}.{nameof(AuthController.Login)}"] = (Lifetime.PersonsChoice, typeof(LoginCommand)),
        [$"{nameof(AuthController)}.{nameof(AuthController.ExternalLogin)}"] = (Lifetime.PersonsChoice, typeof(ExternalLoginCommand)),
        [$"{nameof(TwoFactorController)}.{nameof(TwoFactorController.Verify)}"] = (Lifetime.PersonsChoice, typeof(VerifyTwoFactorLoginCommand)),
        [$"{nameof(AuthController)}.{nameof(AuthController.CompleteRegistration)}"] = (Lifetime.BrowserSession, typeof(CompleteRegistrationCommand)),
        [$"{nameof(AuthController)}.{nameof(AuthController.VerifyEmail)}"] = (Lifetime.BrowserSession, typeof(VerifyEmailCommand)),
        [$"{nameof(AuthController)}.{nameof(AuthController.RecoverAccount)}"] = (Lifetime.BrowserSession, typeof(RecoverAccountCommand)),
        [$"{nameof(AuthController)}.{nameof(AuthController.RecoverAccountExternal)}"] = (Lifetime.BrowserSession, typeof(RecoverAccountExternalCommand)),
        // OI-215: the sign-in by emailed code carries the choice, like the password; its
        // recovery entry signs in for a browser session, like the other two.
        [$"{nameof(AuthController)}.{nameof(AuthController.SignInWithEmailCode)}"] = (Lifetime.PersonsChoice, typeof(SignInWithEmailCodeCommand)),
        [$"{nameof(AuthController)}.{nameof(AuthController.RecoverAccountWithEmailCode)}"] = (Lifetime.BrowserSession, typeof(RecoverAccountWithEmailCodeCommand)),
        [$"{nameof(AuthController)}.{nameof(AuthController.RefreshToken)}"] = (Lifetime.PresentedSession, null),
    };

    private static List<MethodInfo> SessionActions() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(action => action.GetCustomAttribute<IssuesFirstPartySessionAttribute>() is not null)
            .ToList();

    private static Type BodyOf(MethodInfo action) =>
        action.GetParameters().Single(p => p.GetCustomAttribute<FromBodyAttribute>() is not null).ParameterType;

    private static bool HasStaySignedIn(Type type) =>
        type.GetProperty("StaySignedIn") is { PropertyType: var propertyType } && propertyType == typeof(bool);

    [Fact]
    public void EveryActionThatIssuesASession_IsClassified()
    {
        var actions = SessionActions().Select(a => $"{a.DeclaringType!.Name}.{a.Name}").ToList();

        actions.Should().HaveCountGreaterThanOrEqualTo(8, "a floor, so the guard cannot pass by seeing nothing");
        actions.Should().BeEquivalentTo(Exits.Keys,
            "every action marked [IssuesFirstPartySession] says how long its cookies live (OI-154): " +
            "the person's choice, a browser session, or the presented session");
    }

    [Fact]
    public void TheCarryingExits_TakeTheChoiceFromTheBody_AndTheOthersCannotReceiveIt()
    {
        foreach (var action in SessionActions())
        {
            var name = $"{action.DeclaringType!.Name}.{action.Name}";
            var (lifetime, command) = Exits[name];

            if (lifetime == Lifetime.PresentedSession)
            {
                continue;
            }

            var carries = lifetime == Lifetime.PersonsChoice;
            HasStaySignedIn(BodyOf(action)).Should().Be(carries,
                $"{name}'s request body {(carries ? "carries" : "has no")} staySignedIn");
            HasStaySignedIn(command!).Should().Be(carries,
                $"{command!.Name} {(carries ? "carries" : "has no")} StaySignedIn");
        }
    }

    /// <summary>
    /// Each completed sign-in's <c>persistent:</c> argument to the builder, read
    /// from its call, as <see cref="BuildAsyncCallersRepairGuardTests"/> reads
    /// <c>passwordProven:</c>. Absent means the builder's default: a browser
    /// session.
    /// </summary>
    private static readonly Dictionary<string, string> PersistentArgument = new()
    {
        ["LoginCommandHandler.cs"] = "request.StaySignedIn",
        ["ExternalLoginCommandHandler.cs"] = "request.StaySignedIn",
        ["VerifyTwoFactorLoginCommandHandler.cs"] = "request.StaySignedIn",
        ["SignInWithEmailCodeCommandHandler.cs"] = "request.StaySignedIn",
        ["VerifyEmailCommandHandler.cs"] = "<none>",
        ["CompleteRegistrationCommandHandler.cs"] = "<none>",
        ["AccountDeletionRecoverer.cs"] = "<none>",
        ["ExchangeAuthorizationCodeCommandHandler.cs"] = "<none>",
    };

    [Theory]
    [InlineData(typeof(Auth.Application.Interfaces.ILoginResponseBuilder))]
    [InlineData(typeof(Auth.Application.Features.Authentication.Common.LoginResponseBuilder))]
    public void TheBuildersDefault_IsABrowserSession(Type builder)
    {
        // "<none>" below means this default: an exit that does not pass the choice
        // must fail closed. Callers through the interface get the interface's.
        var persistent = builder.GetMethod("BuildAsync")!.GetParameters().Single(p => p.Name == "persistent");

        persistent.HasDefaultValue.Should().BeTrue();
        persistent.DefaultValue.Should().Be(false, $"{builder.Name}.BuildAsync must default to a browser session");
    }

    [Fact]
    public void EveryBuilderCall_PassesTheChoiceOnlyFromTheThreeCarryingCommands()
    {
        var callers = new List<(string Name, string Argument)>();
        foreach (var (file, source) in ApiSourceScan.ProductionSources())
        {
            if (file.EndsWith("LoginResponseBuilder.cs", StringComparison.Ordinal)
                || !Regex.IsMatch(source, @"\bI?LoginResponseBuilder\b"))
            {
                continue;
            }

            foreach (Match call in Regex.Matches(source, @"\.\s*BuildAsync\s*\("))
            {
                var arguments = BuildAsyncCallersRepairGuardTests.Arguments(source, call.Index + call.Length - 1);
                var argument = Regex.Match(arguments, @"persistent:\s*([^,]+?)\s*(?:,|$)");
                callers.Add((Path.GetFileName(file), argument.Success ? argument.Groups[1].Value.Trim() : "<none>"));
            }
        }

        callers.Should().HaveCount(8, "eight sign-in exits today; a new one is listed here with its choice");
        callers.Select(c => c.Name).Should().BeEquivalentTo(PersistentArgument.Keys);
        foreach (var (name, argument) in callers)
        {
            argument.Should().Be(PersistentArgument[name], $"{name} decides how long its sign-in lives");
        }
    }
}
