using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Domain.ValueObjects;

/// <summary>
/// Unit tests for the CardColor value object: the opaque #RRGGBB an email layout's card is
/// drawn in, and the colour its logo background is built in.
/// </summary>
public class CardColorTests
{
    [Theory]
    [InlineData("#3c6e47", "#3C6E47")]
    [InlineData("  #1A1A1C ", "#1A1A1C")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void Create_SixHexDigits_NormalisesToUpperCase(string input, string expected)
    {
        var result = CardColor.Create(input);

        result.IsError.Should().BeFalse();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("#FFF")]        // shorthand: one spelling per colour, so file names are unique
    [InlineData("#3C6E4780")]   // alpha: a see-through background is what the plate prevents
    [InlineData("3C6E47")]
    [InlineData("rgb(60,110,71)")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_AnythingElse_IsRefused(string? input)
    {
        var result = CardColor.Create(input);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutCardColorInvalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateOptional_Empty_MeansTheDefault(string? input)
    {
        var result = CardColor.CreateOptional(input);

        result.IsError.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Channels_ReadTheHexDigits()
    {
        var colour = CardColor.Create("#3C6E47").Value;

        (colour.Red, colour.Green, colour.Blue).Should().Be(((byte)0x3C, (byte)0x6E, (byte)0x47));
    }

    [Theory]
    [InlineData(".card { background-color:#3c6e47; }", true)]
    [InlineData("<td bgcolor=\"#3C6E47\">", true)]
    [InlineData(".card { background-color:#3C6E4780; }", false)]  // a longer colour, not this one
    [InlineData(".card { background-color:#1A1A1C; }", false)]
    public void AppearsIn_FindsTheColourOnlyAsAWholeColour(string html, bool expected)
    {
        CardColor.Create("#3C6E47").Value.AppearsIn(html).Should().Be(expected);
    }

    [Theory]
    [InlineData(".card{background:#fff}", true)]
    [InlineData(".card{background:#FFF;}", true)]
    [InlineData(".card{background:#FFFFFF}", true)]
    [InlineData(".card{background:#FFFE}", false)]
    public void AppearsIn_AcceptsTheShorthandOfTheSameColour(string html, bool expected)
    {
        CardColor.DefaultLight.AppearsIn(html).Should().Be(expected);
    }

    [Fact]
    public void Defaults_AreTheShippedLayoutsCards()
    {
        // The shipped layout (seed 11) draws its cards in these, and every install that sets
        // no colour keeps them, so nothing changes for an existing deployment.
        CardColor.DefaultLight.Value.Should().Be("#FFFFFF");
        CardColor.DefaultDark.Value.Should().Be("#1A1A1C");
        CardColors.Of(null, null).Should().Be(CardColors.Default);
    }
}
