using System.Net;
using System.Text.Json;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// An error response read off a test host, with the assertions every error path shares
/// (ADR 0001): problem+json, a published <c>code</c>, <c>status</c>, <c>traceId</c>, the
/// framework's <c>type</c> and <c>title</c>, and no exception data.
/// </summary>
internal sealed record ProblemResponse(HttpResponseMessage Response, string Raw, JsonElement Body)
{
    /// <summary>Exception text no body may carry; endpoints that throw use it as their message.</summary>
    public const string SecretExceptionText = "connection string Server=db;Password=hunter2";

    public static async Task<ProblemResponse> ReadAsync(HttpClient client, HttpRequestMessage request)
    {
        var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        return new ProblemResponse(response, raw, JsonDocument.Parse(raw).RootElement.Clone());
    }

    public void AssertContract(HttpStatusCode status, string code, string? title = null, string? type = null)
    {
        Assert.Equal(status, Response.StatusCode);
        Assert.Equal("application/problem+json", Response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, Body.GetProperty("code").GetString());
        Assert.True(PublishedErrorCodes.Contains(code), $"'{code}' is not a published code.");
        Assert.Equal((int)status, Body.GetProperty("status").GetInt32());
        Assert.True(Body.TryGetProperty("traceId", out _), "traceId is missing.");
        Assert.Equal(type ?? FrameworkDefaults.TypeFor((int)status), OptionalString("type"));
        Assert.Equal(title ?? FrameworkDefaults.TitleFor((int)status), OptionalString("title"));
        Assert.DoesNotContain("hunter2", Raw);
        Assert.DoesNotContain("Exception", Raw);
        Assert.DoesNotContain("   at ", Raw);
    }

    public string? OptionalString(string name) =>
        Body.TryGetProperty(name, out var value) ? value.GetString() : null;

    /// <summary>
    /// <c>type</c> and <c>title</c> as the framework writes them, captured from a run on .NET 10.
    /// They are the framework's to change between versions; a failure here is a changed default.
    /// </summary>
    private static class FrameworkDefaults
    {
        private static readonly Dictionary<int, (string? Type, string Title)> Defaults = new()
        {
            [400] = ("https://tools.ietf.org/html/rfc9110#section-15.5.1", "Bad Request"),
            [401] = ("https://tools.ietf.org/html/rfc9110#section-15.5.2", "Unauthorized"),
            [403] = ("https://tools.ietf.org/html/rfc9110#section-15.5.4", "Forbidden"),
            [404] = ("https://tools.ietf.org/html/rfc9110#section-15.5.5", "Not Found"),
            [405] = ("https://tools.ietf.org/html/rfc9110#section-15.5.6", "Method Not Allowed"),
            [409] = ("https://tools.ietf.org/html/rfc9110#section-15.5.10", "Conflict"),
            [413] = ("https://tools.ietf.org/html/rfc9110#section-15.5.14", "Content Too Large"),
            [415] = ("https://tools.ietf.org/html/rfc9110#section-15.5.16", "Unsupported Media Type"),
            [429] = (null, "Too Many Requests"),
            [500] = ("https://tools.ietf.org/html/rfc9110#section-15.6.1", "An error occurred while processing your request."),
            [501] = ("https://tools.ietf.org/html/rfc9110#section-15.6.2", "Not Implemented"),
            [502] = ("https://tools.ietf.org/html/rfc9110#section-15.6.3", "Bad Gateway"),
            [503] = ("https://tools.ietf.org/html/rfc9110#section-15.6.4", "Service Unavailable"),
            [504] = ("https://tools.ietf.org/html/rfc9110#section-15.6.5", "Gateway Timeout"),
        };

        public static string? TypeFor(int status) => Defaults[status].Type;

        public static string TitleFor(int status) => Defaults[status].Title;
    }
}
