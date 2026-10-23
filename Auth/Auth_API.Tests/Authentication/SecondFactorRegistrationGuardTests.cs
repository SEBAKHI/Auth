using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// One proof strategy and one settler for each second-factor method (review 99, F9 (b)).
/// Both are chosen by the method, never by a branch, and neither mistake shows at
/// startup: the verifier refuses two strategies for one method only when it is first
/// built, at the first two-factor request after a deploy, and a method with no strategy
/// or no settler throws only when a code of that kind arrives. This reads the API's own
/// registrations and the store's table, so either mistake fails here instead.
/// </summary>
public class SecondFactorRegistrationGuardTests
{
    private static readonly SecondFactorMethod[] Methods = Enum.GetValues<SecondFactorMethod>();

    [Fact]
    public void EveryMethod_HasExactlyOneRegisteredStrategy()
    {
        var program = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API", "Program.cs"));
        var registered = Regex.Matches(program, @"AddScoped<ISecondFactorProofStrategy, (\w+)>\(\)")
            .Select(match => match.Groups[1].Value)
            .ToList();
        var strategies = typeof(ISecondFactorProofStrategy).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ISecondFactorProofStrategy).IsAssignableFrom(type))
            .ToDictionary(type => type.Name);

        registered.Should().OnlyHaveUniqueItems("a strategy registered twice makes the verifier refuse to build");
        registered.Should().BeEquivalentTo(strategies.Keys, "every strategy is registered, and nothing else is");

        // Each strategy's method is a constant of its type: read without its dependencies.
        registered
            .Select(name => ((ISecondFactorProofStrategy)RuntimeHelpers.GetUninitializedObject(strategies[name])).Method)
            .Should().BeEquivalentTo(Methods, "each method has exactly one strategy, and each strategy its own method");
    }

    [Fact]
    public void EveryMethod_HasASettler()
    {
        var settlers = (IDictionary)typeof(TwoFactorStateStore)
            .GetField("Settlers", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        // A dictionary holds one settler per key, so equal key sets mean exactly one each.
        settlers.Keys.Cast<SecondFactorMethod>().Should().BeEquivalentTo(Methods,
            "a commit looks its settler up by the proof's method and has no fallback");
    }
}
