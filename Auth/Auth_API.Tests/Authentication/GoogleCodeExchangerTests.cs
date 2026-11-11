using System.Net;
using System.Text;
using Auth.Application.Configuration;
using Auth.Domain.Errors;
using Auth.Infrastructure.Authentication;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S02 PR D: <see cref="GoogleCodeExchanger"/>, the one call the Google sign-in makes to Google's
/// token endpoint, over a fake handler: what it sends, what it keeps of Google's answer, how each
/// refusal and outage comes back, and that nothing secret reaches a log line.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>redirect_uri</c> left out of the form; the access
/// token returned in place of the ID token; <c>invalid_grant</c> mapped to ProviderNotConfigured;
/// a 5xx returned as CodeExchangeFailed instead of thrown; the secret check removed (the request
/// is sent with an empty secret); the client secret written into the refusal's log line.
/// </remarks>
public sealed class GoogleCodeExchangerTests
{
    private const string ClientId = "test-client.apps.googleusercontent.com";
    private const string ClientSecret = "GOCSPX-test-secret-value-0123456789";
    private const string Code = "4/0Ab-the-one-time-code-from-the-callback";
    private const string Verifier = "dBjftJeZ4CVP-mJ92K9FTB9e42p5hj6E2iZlqZzX7pN0aQ";
    private const string RedirectUri = "https://accounts.example.com/google-callback.html";
    private const string IdToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjMifQ.the-id-token-signature";
    private const string AccessToken = "ya29.the-access-token-nobody-keeps";
    private const string RefreshToken = "1//the-refresh-token-nobody-keeps";

    private static readonly GoogleAuthSettings Configured = new()
    {
        Enabled = true,
        ClientId = ClientId,
        ClientSecret = ClientSecret,
    };

    private static (GoogleCodeExchanger Exchanger, CapturingLogger<GoogleCodeExchanger> Logs) Create(
        HttpMessageHandler handler,
        GoogleAuthSettings? google = null,
        TimeSpan? timeout = null)
    {
        var logs = new CapturingLogger<GoogleCodeExchanger>();
        var client = new HttpClient(handler) { Timeout = timeout ?? GoogleCodeExchanger.Timeout };
        var exchanger = new GoogleCodeExchanger(
            client,
            TestHelpers.CreateOptions(new ExternalAuthSettings { Google = google ?? Configured }),
            logs);
        return (exchanger, logs);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static FakeHttpMessageHandler Answering(HttpStatusCode status, string json) =>
        new(_ => Json(status, json));

    private static readonly string Success =
        $$"""{"access_token":"{{AccessToken}}","expires_in":3599,"refresh_token":"{{RefreshToken}}","scope":"openid email profile","token_type":"Bearer","id_token":"{{IdToken}}"}""";

    [Fact]
    public async Task Exchange_PostsTheSixFormFieldsToGooglesTokenEndpoint_Exactly()
    {
        string? body = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, Success);
        });
        var (exchanger, _) = Create(handler);

        await exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://oauth2.googleapis.com/token"));
        var form = body!.Split('&')
            .Select(pair => pair.Split('='))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
        form.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = Code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = Verifier,
        }, "Google compares the redirect URI and the verifier with the ones the sign-in sent, and nothing else is asked for");
    }

    [Fact]
    public async Task Exchange_WhenGoogleAnswers_ReturnsTheIdTokenAndNothingElse()
    {
        var (exchanger, _) = Create(Answering(HttpStatusCode.OK, Success));

        var result = await exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be(IdToken);
        result.Value.Should().NotContain(AccessToken).And.NotContain(RefreshToken,
            "nothing here calls a Google API for the person, so their access and refresh tokens never leave");
    }

    [Theory]
    [InlineData("invalid_grant")]
    [InlineData("invalid_request")]
    [InlineData("access_denied")]
    public async Task Exchange_WhenGoogleRefusesTheCode_ReturnsCodeExchangeFailed(string error)
    {
        var (exchanger, _) = Create(Answering(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}","error_description":"Bad Request"}"""));

        var result = await exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        result.FirstError.Should().Be(ExternalAuthErrors.CodeExchangeFailed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "invalid_client")]
    [InlineData(HttpStatusCode.BadRequest, "unauthorized_client")]
    [InlineData(HttpStatusCode.BadRequest, "redirect_uri_mismatch")]
    public async Task Exchange_WhenGoogleRefusesOurConfiguration_ReturnsProviderNotConfigured_AndLogsAnError(
        HttpStatusCode status, string error)
    {
        var (exchanger, logs) = Create(Answering(status, $$"""{"error":"{{error}}"}"""));

        var result = await exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        result.FirstError.Code.Should().Be(ExternalAuthErrors.ProviderNotConfigured("google").Code);
        logs.Entries.Should().Contain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error
            && entry.Message.Contains(error), "the operator must learn which part of the client is wrong");
    }

    [Fact]
    public async Task Exchange_WhenGoogleAnswers5xx_ThrowsAnOutage()
    {
        var (exchanger, _) = Create(Answering(HttpStatusCode.ServiceUnavailable, "<html>down</html>"));

        var act = () => exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        OutageClassifier.IsOutage(thrown.Which).Should().BeTrue(
            "the global handler answers an outage 503 Http.Unavailable with Retry-After (ADR 0001 §5)");
    }

    /// <summary>
    /// Not an OAuth refusal, so not "try again": Google throttling this server, or an outbound
    /// proxy's error page in place of Google's JSON (review of #162).
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":"rate_limit_exceeded"}""")]
    [InlineData(HttpStatusCode.Forbidden, "<html>proxy says no</html>")]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired, "")]
    public async Task Exchange_WhenTheAnswerIsNotAnOAuthRefusal_ThrowsAnOutage(HttpStatusCode status, string body)
    {
        var (exchanger, _) = Create(new FakeHttpMessageHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }));

        var act = () => exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        OutageClassifier.IsOutage(thrown.Which).Should().BeTrue();
    }

    /// <summary>
    /// REVIEW 162 F2: a redirect is an answer, never followed (the registration turns redirects
    /// off): without an OAuth body it is the outage path, after one request only.
    /// </summary>
    [Fact]
    public async Task Exchange_WhenAnsweredWithARedirect_ThrowsAnOutage_AfterOneRequest()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location = new Uri("https://elsewhere.example.net/token");
            return redirect;
        });
        var (exchanger, _) = Create(handler);

        var act = () => exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        OutageClassifier.IsOutage(thrown.Which).Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Exchange_WhenGoogleDoesNotAnswerInTime_ThrowsAnOutage()
    {
        var handler = new HangingHttpMessageHandler();
        var (exchanger, _) = Create(handler, timeout: TimeSpan.FromMilliseconds(100));

        var act = () => exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<TaskCanceledException>();
        OutageClassifier.IsOutage(thrown.Which).Should().BeTrue("HttpClient's own timeout is an outage, not a cancellation");
        handler.Requests.Should().Be(1, "one attempt: the code is single-use");
    }

    [Fact]
    public void Exchange_TimeoutIsFiveSeconds()
    {
        GoogleCodeExchanger.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    public static TheoryData<GoogleAuthSettings?> Unconfigured() => new()
    {
        null,
        new GoogleAuthSettings { Enabled = false, ClientId = ClientId, ClientSecret = ClientSecret },
        new GoogleAuthSettings { Enabled = true, ClientId = "", ClientSecret = ClientSecret },
        new GoogleAuthSettings { Enabled = true, ClientId = ClientId, ClientSecret = null },
        new GoogleAuthSettings { Enabled = true, ClientId = ClientId, ClientSecret = "  " },
    };

    [Theory]
    [MemberData(nameof(Unconfigured))]
    public async Task Exchange_WithoutGoogleOnAndItsIdAndSecret_ReturnsProviderNotConfigured_WithNoCall(GoogleAuthSettings? google)
    {
        var handler = Answering(HttpStatusCode.OK, Success);
        var logs = new CapturingLogger<GoogleCodeExchanger>();
        var exchanger = new GoogleCodeExchanger(
            new HttpClient(handler),
            TestHelpers.CreateOptions(new ExternalAuthSettings { Google = google }),
            logs);

        var result = await exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        result.FirstError.Code.Should().Be(ExternalAuthErrors.ProviderNotConfigured("google").Code);
        handler.Requests.Should().BeEmpty("nothing is sent to Google without the secret");
    }

    [Fact]
    public async Task Exchange_WhenGoogleAnswersWithoutAnIdToken_ReturnsCodeExchangeFailed()
    {
        var (exchanger, _) = Create(Answering(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken}}","token_type":"Bearer"}"""));

        var result = await exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);

        result.FirstError.Should().Be(ExternalAuthErrors.CodeExchangeFailed);
    }

    /// <summary>
    /// Nothing secret in any line, for every way an exchange ends: the code, the verifier, the
    /// client secret, and every token of Google's answer. A refusal's reflected value is reduced
    /// to the RFC 6749 shape, so a body Google never sent cannot write itself into the log either.
    /// </summary>
    [Fact]
    public async Task Exchange_LogsNoCodeVerifierSecretOrToken_WhetherItSucceedsOrFails()
    {
        var answers = new Func<HttpResponseMessage>[]
        {
            () => Json(HttpStatusCode.OK, Success),
            () => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""),
            () => Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}"""),
            () => Json(HttpStatusCode.BadRequest, $$"""{"error":"{{ClientSecret}} {{Code}}"}"""),
            () => Json(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken}}"}"""),
            () => Json(HttpStatusCode.InternalServerError, $$"""{"error":"{{RefreshToken}}"}"""),
        };
        var logged = new List<string>();

        foreach (var answer in answers)
        {
            var (exchanger, logs) = Create(new FakeHttpMessageHandler(_ => answer()));
            try
            {
                await exchanger.ExchangeAsync(Code, Verifier, RedirectUri, CancellationToken.None);
            }
            catch (HttpRequestException exception)
            {
                logged.Add(exception.Message);
            }

            logged.AddRange(logs.Entries.Select(entry => entry.Message));
            logged.AddRange(logs.Entries.SelectMany(entry => entry.Values));
        }

        logged.Should().NotBeEmpty();
        foreach (var secret in new[] { Code, Verifier, ClientSecret, IdToken, AccessToken, RefreshToken })
        {
            logged.Should().NotContain(line => line.Contains(secret), "{0} must never reach a log line", secret);
        }
    }
}
