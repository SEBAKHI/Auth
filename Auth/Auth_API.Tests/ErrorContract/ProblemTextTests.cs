using Auth.Shared.Http.ErrorContract;
using Auth_Localization.Resources.Errors;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// <see cref="ProblemText"/> builds <c>detail</c> while the error response is being written, so a
/// sentence whose placeholders do not fit its arguments must leave <c>detail</c> out rather than
/// throw: an exception there would turn a clean 400 or 404 into a 500 on the path least exercised.
/// </summary>
public class ProblemTextTests
{
    [Fact]
    public void Describe_WithMatchingArguments_ReturnsTheFormattedSentence()
    {
        var text = Text("Password must be at least {0} characters long.");

        Assert.Equal("Password must be at least 24 characters long.", text.Describe("Password.TooShort", [24]));
    }

    [Fact]
    public void Describe_WithTooFewArguments_ReturnsNull()
    {
        var text = Text("Locked until {0} by {1}.");

        Assert.Null(text.Describe("User.AccountLockedUntil", ["2026-01-01"]));
    }

    [Fact]
    public void Describe_WithMalformedPlaceholder_ReturnsNull()
    {
        var text = Text("Missing user {0");

        Assert.Null(text.Describe("User.NotFound", ["abc"]));
    }

    [Fact]
    public void Describe_WithoutArguments_ReturnsTheSentenceUnformatted()
    {
        // The special-character rule quotes braces; unformatted, they are text, not placeholders.
        var text = Text("Password must contain one of !@#$%^&*()-_=+[]{}|;:'\",.<>?/");

        Assert.Equal(
            "Password must contain one of !@#$%^&*()-_=+[]{}|;:'\",.<>?/",
            text.Describe("Password.RequiresSpecialCharacter", null));
    }

    [Fact]
    public void Describe_WithUnknownCode_ReturnsNull()
    {
        Assert.Null(Text(sentence: null).Describe("Nowhere.Unknown", null));
    }

    private static ProblemText Text(string? sentence)
    {
        var localizer = new Mock<IStringLocalizer<DomainErrors>>();
        localizer
            .Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => sentence is null
                ? new LocalizedString(name, name, resourceNotFound: true)
                : new LocalizedString(name, sentence, resourceNotFound: false));

        return new ProblemText(localizer.Object, NullLogger<ProblemText>.Instance);
    }
}
