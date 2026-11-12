using Auth.Application.Behaviors;
using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.ForgotPassword;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.StartEmailCodeSignIn;
using Auth.Application.Features.Authentication.StartRegistration;
using Auth.Application.Features.Platform.GetPasswordPolicy;
using Auth.Domain.Errors;
using Auth_API.Tests.Helpers;
using ErrorOr;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS1 (BD3, BD5-BD7): the behavior verifies a protected request's token before its handler
/// runs, so before any database work; Off reads nothing, Monitor refuses nothing, Enforce refuses a
/// missing or refused token with 400, the configuration is judged before the token, and a check
/// that cannot run follows the fail mode.
/// </summary>
/// <remarks>
/// The real verifier and fail-mode policy over a fake siteverify client: the configuration-first
/// rule lives in the verifier, and these tests see it. Deliberate break, verified red: the
/// verifier judging the token before the configuration (an unconfigured deployment then refuses
/// a request without a token).
/// </remarks>
public class BotDefenseBehaviorTests
{
    private readonly FakeSiteverifyClient _client = new();
    private readonly RecordingVerdicts _verdicts = new();
    private readonly CountingAllowance _allowance = new(permits: 5);
    private int _handlerRuns;

    private BotDefenseBehavior<TRequest, ErrorOr<TResponse>> Behavior<TRequest, TResponse>(
        BotDefenseSettings settings, bool production = false)
        where TRequest : MediatR.IRequest<ErrorOr<TResponse>> =>
        new(
            TestHelpers.CreateOptions(settings),
            BotDefenseTestDoubles.Verifier(settings, _client, production),
            new BotDefenseFailurePolicy(_allowance),
            _verdicts);

    private Task<ErrorOr<LoginResponse>> SignIn(BotDefenseSettings settings, string? token, bool production = false) =>
        Behavior<LoginCommand, LoginResponse>(settings, production).Handle(
            new LoginCommand("person@example.com", "a password", "203.0.113.7", "agent", BotToken: token),
            _ =>
            {
                _handlerRuns++;
                return Task.FromResult<ErrorOr<LoginResponse>>(Error.Validation("Handler.Ran", "the handler ran"));
            },
            CancellationToken.None);

    [Fact]
    public async Task Handle_Off_CallsNothingAndRunsTheHandler()
    {
        await SignIn(BotDefenseTestDoubles.Settings(BotDefenseMode.Off), token: null);

        _handlerRuns.Should().Be(1);
        _client.Calls.Should().BeEmpty("Off reads no token and calls nobody");
        _verdicts.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Monitor_RecordsARefusedTokenAndStillRunsTheHandler()
    {
        _client.Answers(BotDefenseTestDoubles.Refused("invalid-input-response"));

        await SignIn(BotDefenseTestDoubles.Settings(BotDefenseMode.Monitor), token: "a-token");

        _handlerRuns.Should().Be(1, "Monitor protects nobody: it measures");
        _verdicts.Recorded.Should().ContainSingle()
            .Which.Verdict.Kind.Should().Be(BotDefenseVerdictKind.Rejected);
    }

    [Fact]
    public async Task Handle_Monitor_WithoutAToken_RecordsMissing()
    {
        await SignIn(BotDefenseTestDoubles.Settings(BotDefenseMode.Monitor), token: null);

        _handlerRuns.Should().Be(1);
        _verdicts.Recorded.Single().Verdict.Kind.Should().Be(BotDefenseVerdictKind.Missing);
    }

    [Fact]
    public async Task Handle_EnforceWithoutAToken_ReturnsTokenMissing_AndNeverRunsTheHandler()
    {
        var result = await SignIn(BotDefenseTestDoubles.Settings(), token: null);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(BotDefenseErrors.TokenMissing.Code);
        result.FirstError.Type.Should().Be(ErrorType.Validation, "a 400, never a 500");
        _handlerRuns.Should().Be(0, "the check runs before the handler, so before any database read");
        _client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EnforceWithARefusedToken_ReturnsTokenRejected_AndNeverRunsTheHandler()
    {
        _client.Answers(BotDefenseTestDoubles.Refused("timeout-or-duplicate"));

        var result = await SignIn(BotDefenseTestDoubles.Settings(), token: "a-spent-token");

        result.FirstError.Code.Should().Be(BotDefenseErrors.TokenRejected.Code);
        _handlerRuns.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EnforceWithAnAcceptedToken_RunsTheHandler()
    {
        _client.Answers(BotDefenseTestDoubles.Accepted(BotDefenseActions.Login));

        await SignIn(BotDefenseTestDoubles.Settings(), token: "a-good-token");

        _handlerRuns.Should().Be(1);
        _verdicts.Recorded.Single().Verdict.Kind.Should().Be(BotDefenseVerdictKind.Passed);
        _client.Calls.Single().Should().Be((BotDefenseTestDoubles.Secret, "a-good-token"));
    }

    [Fact]
    public async Task Handle_EnforceUnconfigured_RunsTheHandler_EvenWithoutAToken()
    {
        // The configuration before the token: with neither key set there is no check in place,
        // which the posture reports; refusing here would lock everyone out of a deployment that
        // switched Enforce on before setting its keys.
        var result = await SignIn(BotDefenseTestDoubles.Settings(siteKey: "", secret: ""), token: null);

        result.FirstError.Code.Should().Be("Handler.Ran");
        _handlerRuns.Should().Be(1);
        _verdicts.Recorded.Single().Verdict.Kind.Should().Be(BotDefenseVerdictKind.Unconfigured);
    }

    [Fact]
    public async Task Handle_EnforceWithATestingSitekeyInProduction_FollowsTheFailMode()
    {
        var settings = BotDefenseTestDoubles.Settings(siteKey: TurnstileTestingKeys.Sitekeys[0]);

        // A sign-in goes on through the per-address allowance (BD6, PerSurface)...
        await SignIn(settings, token: TurnstileTestingKeys.DummyToken, production: true);
        _handlerRuns.Should().Be(1);
        _allowance.Asked.Should().ContainSingle().Which.Should().Be("203.0.113.7");
        _client.Calls.Should().BeEmpty("a testing key in Production is a misconfiguration, judged before any call");

        // ...and a sign-up start is refused.
        var signUp = await Behavior<StartRegistrationCommand, StartRegistrationResponse>(settings, production: true).Handle(
            new StartRegistrationCommand("new@example.com", "en", "203.0.113.7", "agent", TurnstileTestingKeys.DummyToken),
            _ => throw new InvalidOperationException("the handler must not run"),
            CancellationToken.None);
        signUp.FirstError.Code.Should().Be(BotDefenseErrors.Unavailable.Code);
    }

    [Fact]
    public async Task Handle_EnforceWhileCloudflareIsUnreachable_SignInTakesAnAllowancePermit_ThenIsRefused()
    {
        var settings = BotDefenseTestDoubles.Settings();
        for (var i = 0; i < 6; i++)
        {
            _client.Answers(BotDefenseTestDoubles.NoAnswer());
        }

        for (var i = 0; i < 5; i++)
        {
            (await SignIn(settings, token: "a-token")).FirstError.Code.Should().Be("Handler.Ran");
        }

        var sixth = await SignIn(settings, token: "a-token");
        sixth.FirstError.Code.Should().Be(BotDefenseErrors.Unavailable.Code);
        _handlerRuns.Should().Be(5);
    }

    [Fact]
    public async Task Handle_TheCodeStart_IsRefusedWithoutAToken_AndTakesTheAllowanceWhenCloudflareIsUnreachable()
    {
        var settings = BotDefenseTestDoubles.Settings();
        Task<ErrorOr<StartRegistrationResponse>> Start(string? token) =>
            Behavior<StartEmailCodeSignInCommand, StartRegistrationResponse>(settings).Handle(
                new StartEmailCodeSignInCommand("person@example.com", "en", "203.0.113.7", "agent", token),
                _ =>
                {
                    _handlerRuns++;
                    return Task.FromResult<ErrorOr<StartRegistrationResponse>>(Error.Validation("Handler.Ran", "the handler ran"));
                },
                CancellationToken.None);

        (await Start(null)).FirstError.Code.Should().Be(BotDefenseErrors.TokenMissing.Code);
        _handlerRuns.Should().Be(0);

        _client.Answers(BotDefenseTestDoubles.NoAnswer());
        (await Start("a-token")).FirstError.Code.Should().Be("Handler.Ran", "the code start is in the allowance class (D-S13-1)");
        _allowance.Asked.Should().ContainSingle().Which.Should().Be("203.0.113.7");
        _verdicts.Recorded.Select(record => record.Action).Should().Equal(BotDefenseActions.EmailCode, BotDefenseActions.EmailCode);
    }

    [Fact]
    public async Task Handle_ForgotPassword_IsCheckedWithItsOwnAction()
    {
        // A token made for the sign-in is refused on the reset request: each surface has its action.
        _client.Answers(BotDefenseTestDoubles.Accepted(BotDefenseActions.Login));

        var result = await Behavior<ForgotPasswordCommand, ForgotPasswordResponse>(BotDefenseTestDoubles.Settings()).Handle(
            new ForgotPasswordCommand("person@example.com", "203.0.113.7", "a-token-for-the-sign-in"),
            _ => throw new InvalidOperationException("the handler must not run"),
            CancellationToken.None);

        result.FirstError.Code.Should().Be(BotDefenseErrors.TokenRejected.Code);
        _verdicts.Recorded.Single().Verdict.Codes.Should().Contain("action-mismatch");
    }

    [Fact]
    public async Task Handle_AnUnprotectedRequest_PassesThroughUntouched_UnderEnforce()
    {
        var behavior = Behavior<GetPasswordPolicyQuery, PasswordPolicyDto>(BotDefenseTestDoubles.Settings());
        var ran = false;

        await behavior.Handle(
            new GetPasswordPolicyQuery(),
            _ =>
            {
                ran = true;
                return Task.FromResult<ErrorOr<PasswordPolicyDto>>(Error.Validation("Handler.Ran", "ran"));
            },
            CancellationToken.None);

        ran.Should().BeTrue();
        _client.Calls.Should().BeEmpty();
        _verdicts.Recorded.Should().BeEmpty();
    }
}
