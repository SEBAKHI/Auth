using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Domain.ValueObjects;

/// <summary>
/// The presentation hints of an authorize or end-session request, against
/// <c>docs/api/ui-locales-cases.json</c>: the table the accounts app's resolver
/// reads too, so the two cannot disagree on what a value means.
/// </summary>
public class PresentationHintsTests
{
    public static TheoryData<string?, string?> UiLocalesCases => Cases("uiLocales");

    public static TheoryData<string?, string?> UiThemeCases => Cases("uiTheme");

    [Fact]
    public void Cases_AreNotEmpty()
    {
        UiLocalesCases.Count.Should().BeGreaterThan(10);
        UiThemeCases.Count.Should().BeGreaterThan(5);
    }

    [Theory]
    [MemberData(nameof(UiLocalesCases))]
    public void UiLocales_ResolvesAsTheSharedTableSays(string? input, string? expected)
    {
        PresentationHints.From(input, uiTheme: null).Language.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(UiThemeCases))]
    public void UiTheme_ResolvesAsTheSharedTableSays(string? input, string? expected)
    {
        PresentationHints.From(uiLocales: null, input).Theme.Should().Be(expected);
    }

    [Fact]
    public void NeitherHint_IsNone()
    {
        var hints = PresentationHints.From(null, "blue");

        hints.Should().BeSameAs(PresentationHints.None);
        hints.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AResolvedValue_ReadsAsItself()
    {
        // What a stored request holds goes through the same resolution on a
        // resume, so it must come back unchanged.
        foreach (var language in Languages.Supported)
        {
            PresentationHints.From(language, null).Language.Should().Be(language);
        }

        foreach (var theme in PresentationHints.Themes)
        {
            PresentationHints.From(null, theme).Theme.Should().Be(theme);
        }
    }

    [Fact]
    public void EqualHints_AreEqual()
    {
        PresentationHints.From("ar-EG", "dark").Should().Be(PresentationHints.From("ar", "dark"));
        PresentationHints.From("ar", "dark").Should().NotBe(PresentationHints.From("ar", "light"));
    }

    private static TheoryData<string?, string?> Cases(string list)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(CasesPath()));
        var data = new TheoryData<string?, string?>();
        foreach (var row in document.RootElement.GetProperty(list).EnumerateArray())
        {
            data.Add(row.GetProperty("input").GetString(), row.GetProperty("expected").GetString());
        }

        return data;
    }

    private static string CasesPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "api", "ui-locales-cases.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"docs/api/ui-locales-cases.json was not found walking up from '{AppContext.BaseDirectory}'.");
    }
}
