using Auth.Application.Common.BotDefense;
using Auth.Application.Features.Authentication.ForgotPassword;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.StartEmailCodeSignIn;
using Auth.Application.Features.Authentication.StartRegistration;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS2 (BD1, D1, D-S13-1 (a)): the bot-check marker sits on exactly four commands, each with
/// its own action, and the behavior is registered after validation. A fifth surface, or one of
/// the four losing the marker, fails here.
/// </summary>
/// <remarks>
/// Deliberate break, verified red: the marker removed from <see cref="StartEmailCodeSignInCommand"/>.
/// </remarks>
public class BotDefenseSurfaceGuardTests
{
    private static readonly Dictionary<Type, string> Protected = new()
    {
        [typeof(LoginCommand)] = BotDefenseActions.Login,
        [typeof(StartRegistrationCommand)] = BotDefenseActions.Register,
        [typeof(ForgotPasswordCommand)] = BotDefenseActions.ForgotPassword,
        [typeof(StartEmailCodeSignInCommand)] = BotDefenseActions.EmailCode,
    };

    [Fact]
    public void TheMarker_SitsOnExactlyTheFourAnonymousSurfaces()
    {
        var marked = typeof(IBotProtectedRequest).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IBotProtectedRequest).IsAssignableFrom(type))
            .ToList();

        marked.Should().NotBeEmpty("the scan must find the marked commands, or it proves nothing");
        marked.Should().BeEquivalentTo(Protected.Keys,
            "the four anonymous surfaces that check a password or mail an address carry the check; nothing else does");
    }

    [Fact]
    public void EachSurface_AsksForItsOwnAction()
    {
        object[] commands =
        [
            new LoginCommand("a@example.com", "p", null, null),
            new StartRegistrationCommand("a@example.com", null, null, null),
            new ForgotPasswordCommand("a@example.com"),
            new StartEmailCodeSignInCommand("a@example.com", null, null, null),
        ];
        var requests = commands.Select(command => command.Should().BeAssignableTo<IBotProtectedRequest>().Subject).ToList();

        requests.Select(request => (request.GetType(), request.Action))
            .Should().BeEquivalentTo(Protected.Select(pair => (pair.Key, pair.Value)));
        requests.Select(request => request.Action).Should().OnlyHaveUniqueItems();
        BotDefenseActions.All.Should().BeEquivalentTo(Protected.Values);
        // Turnstile accepts at most 32 characters of [A-Za-z0-9_-].
        BotDefenseActions.All.Should().AllSatisfy(action => action.Should().MatchRegex("^[A-Za-z0-9_-]{1,32}$"));
    }

    [Fact]
    public void TheClientAddress_ReachesTheAllowance()
    {
        object[] commands =
        [
            new LoginCommand("a@example.com", "p", "198.51.100.4", null),
            new StartRegistrationCommand("a@example.com", null, "198.51.100.4", null),
            new ForgotPasswordCommand("a@example.com", "198.51.100.4"),
            new StartEmailCodeSignInCommand("a@example.com", null, "198.51.100.4", null),
        ];

        commands.Should().AllSatisfy(command =>
            command.Should().BeAssignableTo<IBotProtectedRequest>().Which.ClientIp.Should().Be("198.51.100.4"));
    }

    [Fact]
    public void TheBehavior_IsRegisteredAfterValidation()
    {
        var program = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "Program.cs"));

        var validation = program.IndexOf("typeof(Auth.Application.Behaviors.ValidationBehavior<,>)", StringComparison.Ordinal);
        var botDefense = program.IndexOf("typeof(Auth.Application.Behaviors.BotDefenseBehavior<,>)", StringComparison.Ordinal);

        validation.Should().BeGreaterThan(0);
        botDefense.Should().BeGreaterThan(validation,
            "the check runs after the request's shape is valid and before the handler, so before any database work");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
