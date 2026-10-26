using Auth.Application.Interfaces;
using Auth.Domain.Entities;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Whether a sign-in by emailed code must also prove the account's password before it
/// finishes (OI-215, T4 (a)): the account has a password, and it holds any platform
/// permission or the page asked for full proof.
/// </summary>
/// <remarks>
/// <para>
/// Read from the account's platform claims, the first half of the platform
/// administrators' two-factor policy, and NEVER from its enforcement switch
/// (<c>TwoFactor:EnforceForPlatformAdmins</c>): keyed on the switch, a mailbox holder
/// would sign in to a platform administrator whenever the switch is off. The password
/// is then proved by the ordinary sign-in, which counts wrong passwords and opens the
/// second step as it always does.
/// </para>
/// <para>
/// An account without a password is never asked for one, whatever the page asked: it
/// has none to give, and its emailed code is its first factor (D-215-1).
/// </para>
/// </remarks>
public class EmailCodeFullProofRule
{
    private readonly ITokenClaimsResolver _tokenClaimsResolver;

    public EmailCodeFullProofRule(ITokenClaimsResolver tokenClaimsResolver)
    {
        _tokenClaimsResolver = tokenClaimsResolver;
    }

    /// <summary>Whether <paramref name="user"/> must prove its password to finish.</summary>
    /// <param name="requireFullProof">The page's add-only reason.</param>
    public async Task<bool> RequiresPasswordAsync(User user, bool requireFullProof, CancellationToken cancellationToken)
    {
        if (user.PasswordHash is null)
        {
            return false;
        }

        if (requireFullProof)
        {
            return true;
        }

        var platformClaims = await _tokenClaimsResolver.ResolveAsync(user.Id, applicationId: null, cancellationToken);
        return platformClaims.Permissions.Count > 0;
    }
}
