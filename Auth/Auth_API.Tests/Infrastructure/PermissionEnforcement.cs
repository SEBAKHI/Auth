using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth_API.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// What the API actually demands: every permission code a request can be
/// refused for.
/// </summary>
/// <remarks>
/// <para>
/// There are two surfaces, and a guard that reads one of them is wrong about
/// the system. Most codes are demanded by <c>[RequirePermission]</c>, which
/// reflection reads exactly. The rest are demanded inside a controller body,
/// where the code widens a query rather than guarding an endpoint; reflection
/// is blind to those, so they are read from the source text.
/// </para>
/// <para>
/// The in-code half used to be a hand-written array of three codes. Two of the
/// three carried attributes as well, so the array contributed exactly one entry
/// while reading as though it covered three - and had that one call site been
/// deleted, the array would have kept a seeded permission alive with nothing
/// able to say so. Every in-code demand goes through
/// <c>ApiController.HasPermissionClaim</c>, so scanning for it costs nothing
/// and cannot fall behind the code it describes.
/// </para>
/// </remarks>
internal static class PermissionEnforcement
{
    /// <summary>
    /// One in-code demand, written either way.
    /// </summary>
    /// <remarks>
    /// Both forms are read on purpose. The scan has to keep working across the
    /// migration that turns the literals into catalogue references: recognising
    /// only the form that exists today would blind both coverage tests at the
    /// exact commit that changes them, and <c>organizations:manage</c> — which
    /// no attribute carries — would drop out of the enforced set silently.
    /// </remarks>
    private static readonly Regex InCodeDemand = new(
        @"HasPermissionClaim\(\s*(?:""(?<literal>[^""]+)""|PermissionCodes\.(?<group>\w+)\.(?<member>\w+))\s*\)",
        RegexOptions.Compiled);

    /// <summary>Every concrete controller the attribute scan covers.</summary>
    /// <remarks>
    /// Scoped to one assembly. A controller hosted in a future module library
    /// would be missed silently, which is why the coverage tests assert on this
    /// count rather than on the number of codes alone: losing an assembly loses
    /// its codes from both sides of the comparison at once, and a code-count
    /// floor cannot see that.
    /// </remarks>
    internal static IReadOnlyList<Type> Controllers() =>
    [
        .. typeof(PermissionRequirementHandler).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
    ];

    /// <summary>Codes demanded by a <c>[RequirePermission]</c> attribute.</summary>
    internal static IEnumerable<string> FromAttributes() =>
        Controllers()
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Cast<MemberInfo>()
                .Append(type))
            .SelectMany(member => member.GetCustomAttributes<RequirePermissionAttribute>())
            .Select(attribute => attribute.Permission);

    /// <summary>Codes demanded in a controller body rather than by an attribute.</summary>
    internal static IEnumerable<string> FromCode() =>
        ApiSourceScan.ProductionSources()
            .SelectMany(entry => InCodeDemand.Matches(entry.Source))
            .Select(Resolve)
            .OfType<string>();

    /// <summary>The code a match names, written either as a literal or a constant.</summary>
    private static string? Resolve(System.Text.RegularExpressions.Match match) =>
        match.Groups["literal"].Success
            ? match.Groups["literal"].Value
            : typeof(PermissionCodes)
                .GetNestedType(match.Groups["group"].Value, BindingFlags.Public)
                ?.GetField(match.Groups["member"].Value, BindingFlags.Public | BindingFlags.Static)
                ?.GetRawConstantValue() as string;

    /// <summary>Every code the API demands, by either surface.</summary>
    internal static IReadOnlyCollection<string> All() =>
        FromAttributes()
            .Concat(FromCode())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
