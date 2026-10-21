using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auth.Domain.Enums;

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
/// <para>
/// The step obeys the rules the authorize endpoint applies to a fresh sign-in
/// (OI-150). When the page names its stored request and that request demanded
/// one (<c>prompt=login</c>), only a sign-in made after the request was stored,
/// and no older than <see cref="IdentityProviderSettings.StepUpFinishWindow"/>,
/// answers. A well-formed handle that names no live request of this application
/// (expired, missing, or another application's) answers nothing either. In both
/// cases the page's existing 401 path signs the person in again for the same
/// request, so a page left open, or brought back with the Back button, does not
/// list or create anything for the person who left it. A request without
/// <c>login</c> keeps plain single sign-on. Without a handle (the handle switch
/// off, a page older than this rule, or a handle removed from the address) or
/// with a malformed one, the step answers as before; that gap closes with the
/// legacy shape (SUNSET(OI-145)) and the IdP session lifetime.
/// </para>
/// </remarks>
public class OrganizationSetupSession
{
    private readonly IIdpSessionReader _idpSessionReader;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IUserRepository _userRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly OrganizationCreatorRoleCheck _creatorRoleCheck;
    private readonly IAuthorizationRequestRepository _authorizationRequestRepository;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrganizationSetupSession> _logger;

    public OrganizationSetupSession(
        IIdpSessionReader idpSessionReader,
        ICredentialHasher credentialHasher,
        IUserRepository userRepository,
        IApplicationRepository applicationRepository,
        OrganizationCreatorRoleCheck creatorRoleCheck,
        IAuthorizationRequestRepository authorizationRequestRepository,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        TimeProvider timeProvider,
        ILogger<OrganizationSetupSession> logger)
    {
        _idpSessionReader = idpSessionReader;
        _credentialHasher = credentialHasher;
        _userRepository = userRepository;
        _applicationRepository = applicationRepository;
        _creatorRoleCheck = creatorRoleCheck;
        _authorizationRequestRepository = authorizationRequestRepository;
        _idpSettings = idpSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// The user the IdP session cookie belongs to, when the session is valid, the
    /// account may still sign in, and the sign-in still answers the page's stored
    /// request; null otherwise (the caller answers 401).
    /// </summary>
    /// <param name="idpSessionToken">The plain IdP session cookie value, if present.</param>
    /// <param name="clientId">The application code the page carries.</param>
    /// <param name="authorizationRequestHandle">
    /// The handle of the stored authorize request the page serves, if it sent one.
    /// A malformed handle counts as none. A well-formed one that names no live
    /// request of this application answers nothing (the request it named can no
    /// longer be resumed, so the person loses nothing by signing in again).
    /// </param>
    /// <param name="mode">
    /// What the read of the session may write (OI-154): the page's state is a
    /// query and writes nothing; the step ends a session found idle, and records
    /// its use itself once it will run (<see cref="RecordUseAsync"/>). Either way
    /// a session gone idle answers nothing.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task<User?> ResolveUserAsync(
        string? idpSessionToken,
        string? clientId,
        string? authorizationRequestHandle,
        IdpSessionReadMode mode,
        CancellationToken cancellationToken) =>
        (await ResolveSignInAsync(idpSessionToken, clientId, authorizationRequestHandle, mode, cancellationToken))?.User;

    /// <summary>
    /// As <see cref="ResolveUserAsync"/>, with the IdP session the user was
    /// resolved from, for the step to record its use once it will run.
    /// </summary>
    public async Task<OrganizationSetupSignIn?> ResolveSignInAsync(
        string? idpSessionToken,
        string? clientId,
        string? authorizationRequestHandle,
        IdpSessionReadMode mode,
        CancellationToken cancellationToken)
    {
        var session = await _idpSessionReader.FindLiveAsync(idpSessionToken, mode, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var user = await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.CanRenewCredentials())
        {
            return null;
        }

        return await StillAnswersTheRequestAsync(session, clientId, authorizationRequestHandle, cancellationToken)
            ? new OrganizationSetupSignIn(user, session.Id)
            : null;
    }

    /// <summary>
    /// Records the step's use of the IdP session it was resolved from (OI-154).
    /// Called only once every check passed, so a refused step keeps nothing alive,
    /// as a refused authorize keeps nothing alive.
    /// </summary>
    /// <returns>False when the session ended since it was read: no session.</returns>
    public Task<bool> RecordUseAsync(OrganizationSetupSignIn signIn, CancellationToken cancellationToken) =>
        _idpSessionReader.RecordUseAsync(signIn.IdpSessionId, cancellationToken);

    /// <summary>
    /// Whether <paramref name="session"/> still answers the stored request the page
    /// names. Always without a usable handle (absent or malformed); never when a
    /// well-formed handle names no live request of this application; and for a
    /// request that demanded a fresh sign-in, only when the session was minted
    /// after the request was stored and within the finish window, the two rules
    /// the authorize endpoint applies to that demand.
    /// </summary>
    private async Task<bool> StillAnswersTheRequestAsync(
        IdpSession session, string? clientId, string? handle, CancellationToken cancellationToken)
    {
        if (!AuthorizationRequestHandle.IsWellFormed(handle))
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var stored = await _authorizationRequestRepository.GetLiveByHandleHashAsync(
            _credentialHasher.Candidates(CredentialPurpose.AuthorizationRequestHandle, handle), now, cancellationToken);

        if (stored is null || !stored.IsLive(now) || !stored.IsFor(clientId))
        {
            // The page outlived its request (or the address was edited): its resume
            // ends on "sign-in request expired" anyway, so nothing is lost by asking
            // to sign in, and nothing is listed for whoever is at the keyboard.
            _logger.LogInformation(
                "Organization step for client {ClientId} refused: the handle names no live request of this application",
                clientId);
            return false;
        }

        if (!PromptValues.TryParse(stored.Prompt, out var prompt) || !prompt.Login)
        {
            return true;
        }

        if (session.CreatedAt >= stored.CreatedAt &&
            _idpSettings.IsWithinStepUpFinishWindow(session.CreatedAt, now))
        {
            return true;
        }

        _logger.LogInformation(
            "Organization step for client {ClientId} refused: no sign-in after pending request {AuthorizationRequestId} within the finish window",
            clientId, stored.Id);
        return false;
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

/// <summary>The user the step acts for, and the IdP session they were resolved from.</summary>
public sealed record OrganizationSetupSignIn(User User, Guid IdpSessionId);

/// <summary>The application asking for the step, and its usable creator role.</summary>
public sealed record OrganizationSetupContext(
    Auth.Domain.Entities.Application Application,
    Guid CreatorRoleId);
