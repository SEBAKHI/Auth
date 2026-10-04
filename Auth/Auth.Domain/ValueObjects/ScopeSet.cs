using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// A set of OAuth scopes: what a client asked for, what an application is
/// allowed, or what a sign-in was granted. Always contains
/// <see cref="OAuthScopes.OpenId"/>, which every application has and every grant
/// carries, so the grant rule <c>{openid} ∪ (requested ∩ allowed)</c> is a plain
/// <see cref="Intersect"/>.
/// </summary>
/// <remarks>
/// Its canonical text is space-delimited, without duplicates, in the fixed order
/// of <see cref="OAuthScopes.Supported"/> (<c>openid profile email phone</c>):
/// the value stored with a code and a refresh token, written into the access
/// token's <c>scope</c> claim and returned as the token response's <c>scope</c>.
/// </remarks>
public sealed class ScopeSet : IEquatable<ScopeSet>
{
    /// <summary>
    /// The longest <c>scope</c> parameter accepted. Four names fit in 26
    /// characters; the bound only stops an unbounded value from being split.
    /// </summary>
    public const int MaxLength = 512;

    /// <summary>
    /// The set that identifies the user and nothing more: the grant of a request
    /// that names no scope, and the allowed set of an application nobody has
    /// configured.
    /// </summary>
    public static readonly ScopeSet OpenIdOnly = new([OAuthScopes.OpenId]);

    private ScopeSet(IReadOnlyList<string> names)
    {
        Names = names;
        Value = string.Join(' ', names);
    }

    /// <summary>
    /// Gets the scope names in canonical order, <see cref="OAuthScopes.OpenId"/> first.
    /// </summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Gets the canonical space-delimited text, for example <c>openid profile email</c>.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the names other than <see cref="OAuthScopes.OpenId"/>, in canonical
    /// order: what an administrator chose for an application.
    /// </summary>
    public IReadOnlyList<string> OptionalNames => [.. Names.Skip(1)];

    /// <summary>
    /// Gets the canonical text of <see cref="OptionalNames"/>, or null when there
    /// are none. The stored form of an application's allowed scopes: NULL means
    /// <c>openid</c> only.
    /// </summary>
    public string? OptionalValue => Names.Count > 1 ? string.Join(' ', OptionalNames) : null;

    /// <summary>
    /// The outcome of reading a client's <c>scope</c> parameter.
    /// </summary>
    public enum ParseStatus
    {
        /// <summary>Every name is known; the set is usable.</summary>
        Parsed,

        /// <summary>Too long, or a character outside the RFC 6749 §3.3 scope-token set.</summary>
        Malformed,

        /// <summary>Well formed, but names a scope this server does not know.</summary>
        Unknown,
    }

    /// <summary>
    /// Reads the <c>scope</c> parameter of an authorize request (RFC 6749 §3.3).
    /// </summary>
    /// <remarks>
    /// Strict, because a typo must fail at the first test rather than silently
    /// drop the data the application wanted: names are case-sensitive, a
    /// name the server does not know is <see cref="ParseStatus.Unknown"/>, and a
    /// value over <see cref="MaxLength"/> or with a character outside
    /// <c>%x21 / %x23-5B / %x5D-7E</c> is <see cref="ParseStatus.Malformed"/>.
    /// Repeated, leading and trailing spaces are tolerated and duplicates
    /// collapse. <see cref="OAuthScopes.OfflineAccess"/> is accepted and adds
    /// nothing. A missing or empty parameter is <see cref="OpenIdOnly"/>.
    /// <para>
    /// Returns a status rather than an <see cref="ErrorOr{TValue}"/>: OAuth
    /// prescribes the answer (<c>error=invalid_scope</c> on the client's
    /// redirect), so there is no catalog error to carry.
    /// </para>
    /// </remarks>
    /// <param name="value">The raw parameter, or null when the request has none.</param>
    /// <param name="scopes">The requested set when parsed; <see cref="OpenIdOnly"/> otherwise.</param>
    public static ParseStatus TryParse(string? value, out ScopeSet scopes)
    {
        scopes = OpenIdOnly;

        if (string.IsNullOrEmpty(value))
        {
            return ParseStatus.Parsed;
        }

        if (value.Length > MaxLength || !value.All(c => c == ' ' || IsScopeTokenChar(c)))
        {
            return ParseStatus.Malformed;
        }

        var names = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in names)
        {
            if (!OAuthScopes.IsSupported(name) &&
                !string.Equals(name, OAuthScopes.OfflineAccess, StringComparison.Ordinal))
            {
                return ParseStatus.Unknown;
            }
        }

        scopes = Canonical(names);
        return ParseStatus.Parsed;
    }

    /// <summary>
    /// Builds an application's allowed set from the names an administrator chose.
    /// <see cref="OAuthScopes.OpenId"/> may be included and changes nothing.
    /// </summary>
    /// <returns>
    /// The set, or <c>Application.AllowedScopesInvalid</c> when a name is not one
    /// of <see cref="OAuthScopes.Supported"/> (compared case-sensitively).
    /// </returns>
    public static ErrorOr<ScopeSet> FromNames(IEnumerable<string?> names)
    {
        var list = names.ToList();
        if (!list.All(OAuthScopes.IsSupported))
        {
            return ApplicationErrors.AllowedScopesInvalid;
        }

        return Canonical(list.OfType<string>());
    }

    /// <summary>
    /// Rebuilds a set from its stored text: a code's or refresh token's grant, or
    /// an application's allowed scopes. NULL or empty is <see cref="OpenIdOnly"/>,
    /// which is how a row written before scopes existed is read. A name the server
    /// no longer knows is dropped rather than failing the sign-in.
    /// </summary>
    public static ScopeSet FromStored(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? OpenIdOnly
            : Canonical(stored.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The scopes in both sets. With <see cref="OAuthScopes.OpenId"/> in every set,
    /// this is the grant rule: <c>requested.Intersect(allowed)</c> is
    /// <c>{openid} ∪ (requested ∩ allowed)</c>, and a refresh narrows a stored
    /// grant to what the application is allowed now without ever widening it.
    /// </summary>
    public ScopeSet Intersect(ScopeSet other) =>
        Canonical(Names.Where(name => other.Contains(name)));

    /// <summary>
    /// The names in this set that <paramref name="other"/> lacks, in canonical
    /// order: the requested scopes an application's allowed set drops.
    /// </summary>
    public IReadOnlyList<string> Except(ScopeSet other) =>
        [.. Names.Where(name => !other.Contains(name))];

    /// <summary>
    /// Whether this set contains <paramref name="name"/> (case-sensitive).
    /// </summary>
    public bool Contains(string name) => Names.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// The known names among <paramref name="names"/>, plus
    /// <see cref="OAuthScopes.OpenId"/>, in canonical order without duplicates.
    /// </summary>
    private static ScopeSet Canonical(IEnumerable<string> names)
    {
        var present = names.ToHashSet(StringComparer.Ordinal);
        present.Add(OAuthScopes.OpenId);

        return new ScopeSet([.. OAuthScopes.Supported.Where(present.Contains)]);
    }

    /// <summary>
    /// RFC 6749 §3.3: <c>scope-token = 1*( %x21 / %x23-5B / %x5D-7E )</c>, that
    /// is printable ASCII without the space, the double quote and the backslash.
    /// </summary>
    private static bool IsScopeTokenChar(char c) =>
        c == '\x21' || (c >= '\x23' && c <= '\x5B') || (c >= '\x5D' && c <= '\x7E');

    public bool Equals(ScopeSet? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ScopeSet other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Value;
}
