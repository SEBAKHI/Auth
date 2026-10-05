using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.Organizations.OrganizationSetup;

/// <summary>
/// What both organization-setup endpoints establish before anything else: who
/// the user is, by the single sign-on session, and which application asked.
/// </summary>
/// <remarks>
/// The IdP session cookie, not the accounts app's bearer token, on purpose: the
/// authorize endpoint trusts only that cookie, so reading it here makes these
/// endpoints act for exactly the user authorize will issue the code to. The two
/// sessions have different lifetimes and may belong to different accounts.
/// </remarks>
public class OrganizationSetupSession
{
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly OrganizationCreatorRoleCheck _creatorRoleCheck;

    public OrganizationSetupSession(
        IIdpSessionRepository idpSessionRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        OrganizationCreatorRoleCheck creatorRoleCheck)
    {
        _idpSessionRepository = idpSessionRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _creatorRoleCheck = creatorRoleCheck;
    }

    /// <summary>
    /// The user the IdP session cookie belongs to, when the session is valid and
    /// the account may still sign in; null otherwise (the caller answers 401).
    /// </summary>
    public async Task<User?> ResolveUserAsync(string? idpSessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idpSessionToken))
        {
            return null;
        }

        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(idpSessionToken);
        var session = await _idpSessionRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (session is null || !session.IsValid())
        {
            return null;
        }

        var user = await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        return user is not null && user.CanRenewCredentials() ? user : null;
    }

    /// <summary>
    /// The application and its creator role, when the application offers
    /// organization creation right now, and, when <paramref name="requireConfirmedEmail"/>
    /// is set, the user's email is proven. The checks run in this order, each
    /// with its own code.
    /// </summary>
    /// <param name="requireConfirmedEmail">
    /// True for the step itself. The page's state asks without it, so it can
    /// offer the confirmation instead of a refusal with nothing to act on.
    /// </param>
    public async Task<ErrorOr<OrganizationSetupContext>> ResolveContextAsync(
        User user,
        string? clientId,
        bool requireConfirmedEmail,
        CancellationToken cancellationToken)
    {
        var application = string.IsNullOrWhiteSpace(clientId)
            ? null
            : await _applicationRepository.GetByCodeAsync(clientId, cancellationToken);

        var creatorRoleId = application is null
            ? null
            : await _creatorRoleCheck.GetAvailableCreatorRoleAsync(application, cancellationToken);

        if (application is null || creatorRoleId is not Guid roleId)
        {
            return OrganizationErrors.CreationFromApplicationUnavailable;
        }

        // No organization before the email is proven (owner decision D-63-4).
        if (requireConfirmedEmail && !user.EmailConfirmed)
        {
            return UserErrors.EmailNotConfirmed;
        }

        return new OrganizationSetupContext(application, roleId);
    }
}

/// <summary>The application asking for the step, and its usable creator role.</summary>
public sealed record OrganizationSetupContext(
    Auth.Domain.Entities.Application Application,
    Guid CreatorRoleId);
