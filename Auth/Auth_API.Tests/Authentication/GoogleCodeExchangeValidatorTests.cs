using Auth.Application.Features.Authentication.ExchangeGoogleCode;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S02 PR D: the bounds of <see cref="ExchangeGoogleCodeCommandValidator"/>, checked before
/// anything is sent to Google. The code: present, at most 512 characters. The verifier: RFC 7636
/// §4.1, 43 to 128 characters of <c>A-Z a-z 0-9 - . _ ~</c>.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the code's bound raised to 513; the verifier's lower
/// bound lowered to 42; <c>+</c> added to the verifier's character set; NotEmpty removed from the
/// verifier (a missing value passed Matches); the pattern's <c>\z</c> back to <c>$</c> (REVIEW 162
/// F8.1: a verifier ending in a line break passed).
/// </remarks>
public sealed class GoogleCodeExchangeValidatorTests
{
    private const string Redirect = "https://accounts.example.com/google-callback.html";
    private static readonly string Verifier43 = new('a', 43);

    private static IEnumerable<string> Codes(string code, string verifier) =>
        new ExchangeGoogleCodeCommandValidator()
            .Validate(new ExchangeGoogleCodeCommand(code, verifier, Redirect))
            .Errors.Select(error => error.ErrorCode);

    [Fact]
    public void AValidRequest_Passes()
    {
        Codes("4/0Ab-code", "dBjftJeZ4CVP-mJ92K9FTB9e42p5hj6E2iZlqZzX7pN0aQ").Should().BeEmpty();
        Codes(new string('c', 512), Verifier43).Should().BeEmpty();
        Codes("c", new string('Z', 128)).Should().BeEmpty();
        Codes("c", "AZaz09-._~" + new string('x', 33)).Should().BeEmpty("all four punctuation marks are unreserved");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AMissingCode_IsCodeRequired(string? code)
    {
        Codes(code!, Verifier43).Should().Equal("ExternalAuth.CodeRequired");
    }

    [Fact]
    public void ACodeOver512_IsCodeTooLong()
    {
        Codes(new string('c', 513), Verifier43).Should().Equal("ExternalAuth.CodeTooLong");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-verifier-with-a-plus-sign+which-is-reserved-in-rfc-3986")]
    [InlineData("a verifier with spaces that is long enough to pass the bound")]
    public void ABadVerifier_IsCodeVerifierInvalid(string? verifier)
    {
        Codes("c", verifier!).Should().Equal("ExternalAuth.CodeVerifierInvalid");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AVerifierEndingInALineBreak_IsCodeVerifierInvalid(string lineBreak)
    {
        // REVIEW 162 F8.1: .NET's $ also matches before a final line break.
        Codes("c", Verifier43 + lineBreak).Should().Equal("ExternalAuth.CodeVerifierInvalid");
    }

    [Fact]
    public void AVerifierUnder43_IsCodeVerifierInvalid()
    {
        Codes("c", new string('v', 42)).Should().Equal("ExternalAuth.CodeVerifierInvalid");
    }

    [Fact]
    public void AVerifierOver128_IsCodeVerifierInvalid()
    {
        Codes("c", new string('v', 129)).Should().Equal("ExternalAuth.CodeVerifierInvalid");
    }
}
