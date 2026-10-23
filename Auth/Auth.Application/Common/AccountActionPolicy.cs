using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Common;

/// <summary>
/// An action on an account that <see cref="AccountActionPolicy"/> decides.
/// </summary>
public enum AccountAction
{
    /// <summary>Editing the account's profile.</summary>
    Update,

    /// <summary>Setting or removing the account's profile image.</summary>
    ProfileImage,

    /// <summary>Soft-deleting the account.</summary>
    Delete,

    /// <summary>Destroying a soft-deleted account.</summary>
    PermanentDelete,

    /// <summary>Removing one role assignment.</summary>
    RemoveRole,

    /// <summary>Revoking one direct permission grant.</summary>
    RevokePermission,

    /// <summary>An administrator's lock.</summary>
    Lock,

    /// <summary>Lifting a lock.</summary>
    Unlock,

    /// <summary>Re-activating the account.</summary>
    Activate,

    /// <summary>Deactivating the account.</summary>
    Deactivate,

    /// <summary>The account's owner asking for its deletion.</summary>
    RequestOwnDeletion
}

/// <summary>
/// One action on one account by one actor: what <see cref="AccountActionPolicy"/>
/// decides. A removal also names what it removes, at which scope.
/// </summary>
/// <param name="ActorId">The administrator; the account itself for its own deletion.</param>
/// <param name="AccountId">The account acted on.</param>
/// <param name="Action">The action.</param>
public sealed record AccountActionRequest(Guid ActorId, Guid AccountId, AccountAction Action)
{
    /// <summary>For <see cref="AccountAction.RemoveRole"/>: the role removed.</summary>
    public Guid? RemovedRoleId { get; init; }

    /// <summary>For <see cref="AccountAction.RevokePermission"/>: the code of the permission revoked.</summary>
    public string? RemovedPermissionCode { get; init; }

    /// <summary>For the removals: the application the assignment or grant is scoped to; null for the platform.</summary>
    public Guid? ApplicationId { get; init; }

    /// <summary>Removing <paramref name="roleId"/> at the scope <paramref name="applicationId"/>.</summary>
    public static AccountActionRequest RemoveRole(Guid actorId, Guid accountId, Guid roleId, Guid? applicationId) =>
        new(actorId, accountId, AccountAction.RemoveRole) { RemovedRoleId = roleId, ApplicationId = applicationId };

    /// <summary>Revoking the permission <paramref name="permissionCode"/> at the scope <paramref name="applicationId"/>.</summary>
    public static AccountActionRequest RevokePermission(Guid actorId, Guid accountId, string permissionCode, Guid? applicationId) =>
        new(actorId, accountId, AccountAction.RevokePermission) { RemovedPermissionCode = permissionCode, ApplicationId = applicationId };
}

/// <summary>
/// The three rules every administrative action on an account follows, in one place:
/// the handlers that act and the reads that tell the console what to offer ask the
/// same rules, so a control is shown exactly when the action would be accepted.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Never the system account.</b> It authors the seed data and is the
/// reattribution target of purged actor references.</item>
/// <item><b>Only an administrator who covers the account.</b> Acting on another
/// account requires holding every platform permission it holds, read live, by the
/// matching <see cref="PermissionGrantGuard"/> uses — the comparison the two-factor
/// reset makes. Without it, a holder of <c>users:*</c> could strip, lock or delete the
/// platform administrator. An account that holds no platform permission is covered by
/// anyone, so its application roles stay removable by any user administrator. One's
/// own account always passes.</item>
/// <item><b>Never leave the platform without a full administrator</b>, even by one's
/// own act: an account that can sign in and holds <c>*</c> through a platform role or
/// a direct platform grant. Only the actions that can take that away are subject, and
/// only when the account holds <c>*</c>; the count is the one after the action, so
/// removing one of two <c>*</c> roles is allowed. A change that writes runs this rule
/// on grants read under the lock it writes under (<see cref="IAccountAuthorityChange"/>),
/// so two such changes cannot both pass on the same count.</item>
/// </list>
/// <para>
/// "Can sign in" is the sign-in path's own answer, not a new one: the account is not
/// deleted, its status passes <see cref="AuthenticationHelper.CheckAccountStatus(Domain.Enums.UserStatus)"/>,
/// no lock refuses it (<see cref="Domain.ValueObjects.AccountLockState.IsLive"/>) and its
/// email address is confirmed. The password sign-in asks all four; renewal asks fewer
/// (<see cref="Domain.Entities.User.CanRenewCredentials"/> admits an automatic lock and
/// an unconfirmed address), so this takes the stricter: an account counts only if it
/// could sign in from anywhere now.
/// </para>
/// <para>
/// A refusal changes nothing, publishes nothing and logs one warning naming the rule
/// and the action; the warning is its record, not an audit row.
/// </para>
/// </remarks>
public sealed class AccountActionPolicy
{
    private const string Wildcard = "*";

    private readonly IPermissionRepository _permissionRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountActionPolicy> _logger;

    public AccountActionPolicy(
        IPermissionRepository permissionRepository,
        TimeProvider timeProvider,
        ILogger<AccountActionPolicy> logger)
    {
        _permissionRepository = permissionRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Decides <paramref name="request"/> by the three rules. The third, for the
    /// actions it applies to, is judged on the full administrators as read now,
    /// without a lock: for an action that cannot take one away, or a check made before
    /// a single-use code is spent. A write that can take one away asks the overload
    /// that takes its <see cref="IAccountAuthorityChange"/>.
    /// </summary>
    public async Task<ErrorOr<Success>> EnsureAllowedAsync(
        AccountActionRequest request,
        CancellationToken cancellationToken)
    {
        var fullAdministrators = IsSubjectToRuleThree(request.Action)
            ? await _permissionRepository.GetUsersHoldingWildcardPermissionAsync(cancellationToken)
            : [];

        return await EnsureAllowedAsync(request, fullAdministrators, cancellationToken);
    }

    /// <summary>
    /// Decides <paramref name="request"/> by the three rules, the third on the full
    /// administrators <paramref name="change"/> read under its lock. The caller writes
    /// through <paramref name="change"/> only when this succeeds.
    /// </summary>
    public Task<ErrorOr<Success>> EnsureAllowedAsync(
        AccountActionRequest request,
        IAccountAuthorityChange change,
        CancellationToken cancellationToken) =>
        EnsureAllowedAsync(request, change.FullAdministratorGrants, cancellationToken);

    /// <summary>
    /// Reads what deciding any action of <paramref name="actorId"/> on any of
    /// <paramref name="accountIds"/> needs, in three reads whatever their number: the
    /// actor's platform permissions, the accounts' platform permissions, and every
    /// platform grant of <c>*</c>. The capability reads ask the result per account and
    /// per action; nothing is locked, so it tells the console what to offer and never
    /// replaces the check the action itself makes.
    /// </summary>
    public async Task<AccountActionDecisions> DecideForAsync(
        Guid actorId,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var actorPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(actorId, cancellationToken);
        var accountPermissions = await _permissionRepository.GetUsersEffectivePermissionsAsync(accountIds, cancellationToken);
        var fullAdministrators = await _permissionRepository.GetUsersHoldingWildcardPermissionAsync(cancellationToken);

        return new AccountActionDecisions(
            actorId, actorPermissions, accountPermissions, fullAdministrators, _timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<ErrorOr<Success>> EnsureAllowedAsync(
        AccountActionRequest request,
        IReadOnlyList<FullAdministratorGrant> fullAdministrators,
        CancellationToken cancellationToken)
    {
        // Rule 1 needs nothing read, and rule 2 needs both accounts' grants only when
        // the actor acts on someone else.
        IReadOnlyList<string> actorPermissions = [];
        IReadOnlyList<string> accountPermissions = [];
        if (request.AccountId != WellKnownUserIds.System && request.ActorId != request.AccountId)
        {
            actorPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
                request.ActorId, cancellationToken);
            accountPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
                request.AccountId, cancellationToken);
        }

        var refusal = Decide(
            request, actorPermissions, accountPermissions, fullAdministrators, _timeProvider.GetUtcNow().UtcDateTime);
        if (refusal is null)
        {
            return Result.Success;
        }

        _logger.LogWarning(
            "Refused {Action} on account {AccountId} by {ActorId}: rule {Rule}, {Reason}",
            request.Action, request.AccountId, request.ActorId, refusal.Rule, refusal.Reason);

        return refusal.Error;
    }

    /// <summary>
    /// The three rules, on what was read: null when the action is allowed, else the
    /// first rule that refuses it. Every entry point decides here, and nowhere else.
    /// </summary>
    internal static Refusal? Decide(
        AccountActionRequest request,
        IReadOnlyCollection<string> actorPermissions,
        IReadOnlyCollection<string> accountPermissions,
        IReadOnlyList<FullAdministratorGrant> fullAdministrators,
        DateTime nowUtc)
    {
        if (request.AccountId == WellKnownUserIds.System)
        {
            return new Refusal(1, "the system account", SystemAccountError(request.Action));
        }

        if (request.ActorId != request.AccountId
            && !PermissionGrantGuard.Covers(actorPermissions, accountPermissions))
        {
            return new Refusal(2, "the account holds platform permissions the actor does not", UserErrors.AuthorityNotCovered);
        }

        if (IsSubjectToRuleThree(request.Action) && LeavesNoFullAdministrator(request, fullAdministrators, nowUtc))
        {
            return new Refusal(3, "no full administrator who can sign in would remain", LastFullAdministratorError(request.Action));
        }

        return null;
    }

    /// <summary>The actions that can take the last full administrator away.</summary>
    private static bool IsSubjectToRuleThree(AccountAction action) => action is
        AccountAction.RemoveRole
        or AccountAction.RevokePermission
        or AccountAction.Lock
        or AccountAction.Deactivate
        or AccountAction.Delete
        or AccountAction.PermanentDelete
        or AccountAction.RequestOwnDeletion;

    /// <summary>
    /// Whether no full administrator would remain after <paramref name="request"/>,
    /// when the action takes <c>*</c> away from an account that holds it: the account
    /// stops counting when it is locked, deactivated or deleted, or when the removal
    /// takes its last way of holding <c>*</c>.
    /// </summary>
    private static bool LeavesNoFullAdministrator(
        AccountActionRequest request,
        IReadOnlyList<FullAdministratorGrant> fullAdministrators,
        DateTime nowUtc)
    {
        var accountGrants = fullAdministrators.Where(grant => grant.UserId == request.AccountId).ToList();
        var kept = accountGrants.Where(grant => !IsRemovedBy(request, grant)).ToList();
        if (kept.Count == accountGrants.Count)
        {
            // The account does not hold *, or the action leaves it every way it does.
            return false;
        }

        return !fullAdministrators
            .Where(grant => grant.UserId != request.AccountId || kept.Count > 0)
            .Any(grant => CanSignIn(grant, nowUtc));
    }

    /// <summary>Whether <paramref name="request"/> takes away this way of holding <c>*</c>.</summary>
    private static bool IsRemovedBy(AccountActionRequest request, FullAdministratorGrant grant) => request.Action switch
    {
        // Only a platform-scoped assignment counts, so an application-scoped removal
        // of the same role takes nothing.
        AccountAction.RemoveRole => request.ApplicationId is null && grant.RoleId == request.RemovedRoleId,
        AccountAction.RevokePermission => request.ApplicationId is null
            && grant.RoleId is null
            && string.Equals(request.RemovedPermissionCode, Wildcard, StringComparison.Ordinal),
        _ => true
    };

    /// <summary>
    /// Whether the account could sign in now, by the sign-in path's own checks (see
    /// the remarks on <see cref="AccountActionPolicy"/>).
    /// </summary>
    private static bool CanSignIn(FullAdministratorGrant grant, DateTime nowUtc) =>
        !grant.IsDeleted
        && grant.EmailConfirmed
        && !AuthenticationHelper.CheckAccountStatus(grant.LockState.Status).IsError
        && !grant.LockState.IsLive(nowUtc);

    private static Error SystemAccountError(AccountAction action) => action is
        AccountAction.Delete or AccountAction.PermanentDelete or AccountAction.RequestOwnDeletion
            ? UserErrors.CannotDeleteSystemUser
            : UserErrors.CannotModifySystemUser;

    private static Error LastFullAdministratorError(AccountAction action) => action == AccountAction.RemoveRole
        ? RoleErrors.CannotRemoveLastAdminRole
        : UserErrors.LastFullAdministrator;

    /// <summary>Which rule refused an action, why, and what the caller is answered.</summary>
    internal sealed record Refusal(int Rule, string Reason, Error Error);
}

/// <summary>
/// What <see cref="AccountActionPolicy.DecideForAsync"/> read, and the policy's answer
/// for any action of its actor on any of its accounts — computed in memory, so a page
/// of accounts costs no read per account.
/// </summary>
public sealed class AccountActionDecisions
{
    private readonly Guid _actorId;
    private readonly IReadOnlyList<string> _actorPermissions;
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<string>> _accountPermissions;
    private readonly IReadOnlyList<FullAdministratorGrant> _fullAdministrators;
    private readonly DateTime _nowUtc;

    internal AccountActionDecisions(
        Guid actorId,
        IReadOnlyList<string> actorPermissions,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> accountPermissions,
        IReadOnlyList<FullAdministratorGrant> fullAdministrators,
        DateTime nowUtc)
    {
        _actorId = actorId;
        _actorPermissions = actorPermissions;
        _accountPermissions = accountPermissions;
        _fullAdministrators = fullAdministrators;
        _nowUtc = nowUtc;
    }

    /// <summary>The actor every answer is for.</summary>
    public Guid ActorId => _actorId;

    /// <summary>The account's platform permission codes, as the same read saw them.</summary>
    public IReadOnlyList<string> PermissionsOf(Guid accountId) =>
        _accountPermissions.TryGetValue(accountId, out var codes) ? codes : [];

    /// <summary>Whether the actor may take <paramref name="action"/> on the account.</summary>
    public bool Allows(Guid accountId, AccountAction action) =>
        Allows(new AccountActionRequest(_actorId, accountId, action));

    /// <summary>Whether the actor may remove the role assignment at its scope.</summary>
    public bool AllowsRoleRemoval(Guid accountId, Guid roleId, Guid? applicationId) =>
        Allows(AccountActionRequest.RemoveRole(_actorId, accountId, roleId, applicationId));

    /// <summary>Whether the actor may revoke the direct grant at its scope.</summary>
    public bool AllowsPermissionRevocation(Guid accountId, string permissionCode, Guid? applicationId) =>
        Allows(AccountActionRequest.RevokePermission(_actorId, accountId, permissionCode, applicationId));

    private bool Allows(AccountActionRequest request) =>
        AccountActionPolicy.Decide(
            request, _actorPermissions, PermissionsOf(request.AccountId), _fullAdministrators, _nowUtc) is null;
}
