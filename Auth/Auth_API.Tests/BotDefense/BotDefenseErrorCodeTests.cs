using Auth.Domain.Errors;
using Auth_API.Tests.ErrorContract;
using ErrorOr;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 (BD2, ARR-1072): the bot check's refusals are Validation errors, published with status
/// 400 (never 500) and the member they concern, so the pages show the server's sentence for them.
/// The catalog contract tests and the resource coverage test keep the list, the catalog and the
/// seven languages together; this names the four codes and their pointers.
/// </summary>
public class BotDefenseErrorCodeTests
{
    public static readonly TheoryData<string, string?> Codes = new()
    {
        { BotDefenseErrors.TokenMissing.Code, "#/botToken" },
        { BotDefenseErrors.TokenRejected.Code, "#/botToken" },
        { BotDefenseErrors.Unavailable.Code, null },
        { BotDefenseErrors.TestingKeyInProduction.Code, "#/value" },
    };

    [Theory]
    [MemberData(nameof(Codes))]
    public void EachCode_IsPublishedAsA400_WithItsPointer(string code, string? pointer)
    {
        PublishedErrorCodes.All.Should().ContainKey(code);
        var published = PublishedErrorCodes.All[code];
        published.Status.Should().Be(400, "a refused request, not a broken one");
        published.Source.Should().Be("catalog");
        published.Pointer.Should().Be(pointer);
    }

    [Fact]
    public void EveryRefusal_IsAValidationError()
    {
        new[] { BotDefenseErrors.TokenMissing, BotDefenseErrors.TokenRejected, BotDefenseErrors.Unavailable, BotDefenseErrors.TestingKeyInProduction }
            .Should().AllSatisfy(error => error.Type.Should().Be(ErrorType.Validation));
    }
}
