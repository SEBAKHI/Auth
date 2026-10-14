using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Auth.Application.Features.Authentication.Authorize;

/// <summary>
/// The shape of a stored authorize request's handle (OI-145), for every reader
/// of one: the resume, and the organization step (OI-150).
/// </summary>
internal static class AuthorizationRequestHandle
{
    // A handle is what the authorization-code generator produces: 32 random
    // bytes as 64 lowercase hex characters (OI-142). Anything else never reaches
    // the database. \z, not $: in .NET $ also matches before a final newline.
    private static readonly Regex Pattern = new(
        "^[0-9a-f]{64}\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Whether <paramref name="handle"/> has the shape of a handle this server issued.</summary>
    public static bool IsWellFormed([NotNullWhen(true)] string? handle)
        => handle is not null && Pattern.IsMatch(handle);
}
