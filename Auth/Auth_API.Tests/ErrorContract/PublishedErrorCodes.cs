using System.Text.Json;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// The code list the API publishes to its clients, <c>docs/api/error-codes.json</c> (ADR 0001).
/// Read from the source file rather than copied into the tests, so the tests and the clients see
/// the same list: a code is published there before anything may emit it.
/// </summary>
internal static class PublishedErrorCodes
{
    private static readonly Lazy<IReadOnlyDictionary<string, PublishedErrorCode>> Codes = new(Load);

    public static IReadOnlyDictionary<string, PublishedErrorCode> All => Codes.Value;

    public static bool Contains(string code) => All.ContainsKey(code);

    private static IReadOnlyDictionary<string, PublishedErrorCode> Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ListPath()));

        return document.RootElement.GetProperty("codes")
            .EnumerateArray()
            .Select(entry => new PublishedErrorCode(
                entry.GetProperty("code").GetString()!,
                entry.GetProperty("status").GetInt32(),
                entry.GetProperty("source").GetString()!,
                entry.TryGetProperty("pointer", out var pointer) && pointer.ValueKind == JsonValueKind.String
                    ? pointer.GetString()
                    : null))
            .ToDictionary(code => code.Code, StringComparer.Ordinal);
    }

    private static string ListPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "api", "error-codes.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"docs/api/error-codes.json was not found walking up from '{AppContext.BaseDirectory}'.");
    }
}

/// <param name="Source"><c>catalog</c>, <c>transport</c> or <c>challenge</c>.</param>
/// <param name="Pointer">The request-body member a Validation code concerns, or null.</param>
internal sealed record PublishedErrorCode(string Code, int Status, string Source, string? Pointer);
