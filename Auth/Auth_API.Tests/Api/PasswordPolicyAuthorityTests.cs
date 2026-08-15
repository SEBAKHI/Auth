using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Auth_API.Modules.Authentication.Contracts;

namespace Auth_API.Tests.Api;

/// <summary>
/// <c>Password:MinimumLength</c> is the only length rule in the system, and
/// PasswordValidator is the only thing that applies it.
///
/// Two request contracts used to carry <c>[MinLength(8)]</c> of their own on the
/// new password. Model binding runs before the handler, so those eight
/// characters were a hard floor sitting UNDER the configured policy: an operator
/// who lowered the minimum to 6 got a console that saved happily, a sign-up that
/// worked, and a password change that answered 400 — the same setting enforcing
/// two different numbers depending on which endpoint you reached.
///
/// The other five password paths (register, admin create-user, invitation
/// accept, account recovery, login) never had one, which is why the split went
/// unnoticed. A maximum length is a different thing and stays allowed: it bounds
/// what the hasher is asked to chew on, and no policy setting contradicts it.
/// </summary>
public class PasswordPolicyAuthorityTests
{
    [Fact]
    public void NoRequestContract_DeclaresItsOwnPasswordLengthFloor()
    {
        var offenders = new List<string>();

        foreach (var contract in RequestContracts())
        {
            foreach (var property in contract.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(string) ||
                    !property.Name.EndsWith("Password", StringComparison.Ordinal))
                {
                    continue;
                }

                if (property.GetCustomAttribute<MinLengthAttribute>() is { } minLength)
                {
                    offenders.Add($"{contract.Name}.{property.Name} declares [MinLength({minLength.Length})]");
                }

                if (property.GetCustomAttribute<StringLengthAttribute>() is { MinimumLength: > 0 } stringLength)
                {
                    offenders.Add(
                        $"{contract.Name}.{property.Name} declares [StringLength(MinimumLength = {stringLength.MinimumLength})]");
                }
            }
        }

        offenders.Should().BeEmpty(
            "the configured password policy is the only minimum; a contract-level floor overrides it silently");
    }

    private static IEnumerable<Type> RequestContracts() =>
        typeof(ResetPasswordRequest).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           type.Name.EndsWith("Request", StringComparison.Ordinal));
}
