using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
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
/// Removing one permission from a role, by one actor: what
/// <see cref="AccountActionPolicy.BeginRolePermissionRemovalAsync"/> decides.
/// </summary>
/// <param name="ActorId">The administrator.</param>
/// <param name="RoleId">The role.</param>
/// <param name="RoleApplicationId">The role's application; null for a platform role.</param>
/// <param name="PermissionCode">The code of the permission removed.</param>
public sealed record RolePermissionRemovalRequest(Guid ActorId, Guid RoleId, Guid? RoleApplicationId, string PermissionCode);

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
/// own act: an account that can sign in, has a way to authenticate, and holds
/// <c>*</c> without an end date through a platform role or a direct platform grant.
/// Only the actions that can take that away are subject, and only when the account
/// holds <c>*</c>; the count is the one after the action, so removing one of two
/// <c>*</c> roles is allowed. A change that writes opens it with
/// <see cref="BeginAsync"/>, which runs this rule on grants read under the lock the
/// change writes under (<see cref="IAccountAuthorityChange"/>), so two such changes
/// cannot both pass on the same count.</item>
/// </list>
/// <para>
/// Removing a permission from a role follows the second and third rules in their role
/// form (<see cref="BeginRolePermissionRemovalAsync"/>): the remover must hold the
/// code removed, by the same matching, on every role and every code (owner decision
/// D-226-1 (a)) — a role can lose only what its editor could have put in it; and
/// removing <c>*</c> from a platform role, which takes it from every holder at once,
/// is refused when it would leave no full administrator. A holder of <c>*</c> covers
/// every code, and the third rule keeps one, so no granted permission is ever left
/// removable by nobody.
/// </para>
/// <para>
/// "Can sign in" is the sign-in path's own answer, not a new one: the account is not
/// deleted, its status passes <see cref="AuthenticationHelper.CheckAccountStatus(Domain.Enums.UserStatus)"/>,
/// it is not under an administrator's lock (<see cref="Domain.Enums.AccountLockKind.Admin"/>)
/// and its email address is confirmed. An automatic lock — timed or hard, raised by
/// wrong passwords — does not stop it counting: the password sign-in still admits the
/// owner from a familiar source under it, renewal admits it
/// (<see cref="Domain.Entities.User.CanRenewCredentials"/>), and a password reset lifts
/// it. Counting it out would let anyone who knows an administrator's address, by
/// typing wrong passwords, stop that administrator from acting on the other one
/// (owner decision D-101-1 (a)).
/// </para>
/// <para>
/// Two conditions are stricter than signing in, on purpose, because they can only
/// refuse more. The account has a password or a linked external sign-in: an account
/// with neither, such as a seeded one never set up, cannot be the administrator who
/// remains. And the grant has no end date: a <c>*</c> that will expire, even if it has
/// not yet, leaves the platform without a full administrator when it does. No path
/// removes a password or unlinks a sign-in today; one that is added becomes an action
/// subject to rule 3.
/// </para>
/// <para>
/// An account that does not count cannot be the last: acting on a deleted, inactive,
/// administrator-locked or unconfirmed holder of <c>*</c>, one with no way to
/// authenticate, or one that holds <c>*</c> only until a date takes no full
/// administrator away, so rule 3 never refuses it.
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
    private readonly IUserRepository _userRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountActionPolicy> _logger;

    public AccountActionPolicy(
        IPermissionRepository permissionRepository,
        IUserRepository userRepository,
        TimeProvider timeProvider,
        ILogger<AccountActionPolicy> logger)
    {
        _permissionRepository = permissionRepository;
        _userRepository = userRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Decides <paramref name="request"/> by the three rules. The third, for the
    /// actions it applies to, is judged on the full administrators as read now,
    /// without a lock: for an action that cannot take one away, or a check made before
    /// a single-use code is spent. A write that can take one away opens its change
    /// with <see cref="BeginAsync"/> instead.
    /// </summary>
    public async Task<ErrorOr<Success>> EnsureAllowedAsync(
        AccountActionRequest request,
        CancellationToken cancellationToken)
    {
        var permissions = await ReadPermissionsAsync(request, cancellationToken);
        var fullAdministrators = IsSubjectToRuleThree(request.Action)
            ? await _permissionRepository.GetUsersHoldingWildcardPermissionAsync(cancellationToken)
            : [];

        return Judge(request, permissions, fullAdministrators);
    }

    /// <summary>
    /// For a write that can take the last full administrator away: decides the first
    /// two rules on reads made before any lock, then opens the change — the one lock,
    /// and every platform grant of <c>*</c> read under it — and decides the third on
    /// that read. Returns the open change for the caller to write through and commit;
    /// on a refusal nothing is left open. The lock is held from the count to the
    /// commit, never across the reads the first two rules need.
    /// </summary>
    public async Task<ErrorOr<IAccountAuthorityChange>> BeginAsync(
        AccountActionRequest request,
        CancellationToken cancellationToken)
    {
        var permissions = await ReadPermissionsAsync(request, cancellationToken);

        // Rules 1 and 2 first, with no grant of * to count: a refusal here takes no lock.
        var beforeTheLock = Judge(request, permissions, []);
        if (beforeTheLock.IsError)
        {
            return beforeTheLock.Errors;
        }

        var change = await _userRepository.BeginAuthorityChangeAsync(cancellationToken);
        var underTheLock = Judge(request, permissions, change.FullAdministratorGrants);
        if (underTheLock.IsError)
        {
            await change.DisposeAsync();
            return underTheLock.Errors;
        }

        return ErrorOrFactory.From(change);
    }

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

        // One's own profile asks about one account, the actor itself: its permissions
        // are the ones just read.
        var accountPermissions = accountIds.Count == 1 && accountIds.Contains(actorId)
            ? new Dictionary<Guid, IReadOnlyList<string>> { [actorId] = actorPermissions }
            : await _permissionRepository.GetUsersEffectivePermissionsAsync(accountIds, cancellationToken);
        var fullAdministrators = await _permissionRepository.GetUsersHoldingWildcardPermissionAsync(cancellationToken);

        return new AccountActionDecisions(
            actorId, actorPermissions, accountPermissions, fullAdministrators, _timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// For removing a permission from a role: decides whether the actor holds the code
    /// on a read made before any lock, then opens the change — the one lock, and every
    /// platform grant of <c>*</c> read under it — and decides the third rule on that
    /// read. Every removal takes the lock, whatever the code, so the DELETE always runs
    /// in the change. Returns the open change for the caller to write through and
    /// commit; on a refusal nothing is left open.
    /// </summary>
    public async Task<ErrorOr<IAccountAuthorityChange>> BeginRolePermissionRemovalAsync(
        RolePermissionRemovalRequest request,
        CancellationToken cancellationToken)
    {
        var actorPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(request.ActorId, cancellationToken);

        // The coverage rule first, with no grant of * to count: a refusal here takes no lock.
        var beforeTheLock = JudgeRoleRemoval(request, actorPermissions, []);
        if (beforeTheLock.IsError)
        {
            return beforeTheLock.Errors;
        }

        var change = await _userRepository.BeginAuthorityChangeAsync(cancellationToken);
        var underTheLock = JudgeRoleRemoval(request, actorPermissions, change.FullAdministratorGrants);
        if (underTheLock.IsError)
        {
            await change.DisposeAsync();
            return underTheLock.Errors;
        }

        return ErrorOrFactory.From(change);
    }

    /// <summary>
    /// Which of <paramref name="codes"/>, the role's own, the actor may remove from it
    /// now, by the rules the removal enforces. Two reads at most, whatever the number
    /// of codes: the actor's platform permissions, and every platform grant of
    /// <c>*</c> only when the role is a platform role holding it — the one removal the
    /// third rule can refuse. Nothing is locked, so it tells the console what to offer
    /// and never replaces the check the removal itself makes.
    /// </summary>
    public async Task<IReadOnlyList<string>> RemovableRolePermissionsAsync(
        Guid actorId,
        Guid roleId,
        Guid? roleApplicationId,
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken)
    {
        var actorPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(actorId, cancellationToken);
        var fullAdministrators = roleApplicationId is null && codes.Contains(Wildcard, StringComparer.Ordinal)
            ? await _permissionRepository.GetUsersHoldingWildcardPermissionAsync(cancellationToken)
            : [];
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        return codes
            .Where(code => DecideRoleRemoval(
                new RolePermissionRemovalRequest(actorId, roleId, roleApplicationId, code),
                actorPermissions,
                fullAdministrators,
                nowUtc) is null)
            .ToList();
    }

    /// <summary>
    /// What rule 2 compares: both accounts' live platform permissions, read only when
    /// the actor acts on someone else (rule 1 needs nothing read).
    /// </summary>
    private async Task<(IReadOnlyList<string> Actor, IReadOnlyList<string> Account)> ReadPermissionsAsync(
        AccountActionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AccountId == WellKnownUserIds.System || request.ActorId == request.AccountId)
        {
            return ([], []);
        }

        var actor = await _permissionRepository.GetUserEffectivePermissionsAsync(request.ActorId, cancellationToken);
        var account = await _permissionRepository.GetUserEffectivePermissionsAsync(request.AccountId, cancellationToken);
        return (actor, account);
    }

    private ErrorOr<Success> Judge(
        AccountActionRequest request,
        (IReadOnlyList<string> Actor, IReadOnlyList<string> Account) permissions,
        IReadOnlyList<FullAdministratorGrant> fullAdministrators)
    {
        var refusal = Decide(
            request, permissions.Actor, permissions.Account, fullAdministrators, _timeProvider.GetUtcNow().UtcDateTime);
        if (refusal is null)
        {
            return Result.Success;
        }

        _logger.LogWarning(
            "Refused {Action} on account {AccountId} by {ActorId}: rule {Rule}, {Reason}",
            request.Action, request.AccountId, request.ActorId, refusal.Rule, refusal.Reason);

        return refusal.Error;
    }

    private ErrorOr<Success> JudgeRoleRemoval(
        RolePermissionRemovalRequest request,
        IReadOnlyList<string> actorPermissions,
        IReadOnlyList<FullAdministratorGrant> fullAdministrators)
    {
        var refusal = DecideRoleRemoval(
            request, actorPermissions, fullAdministrators, _timeProvider.GetUtcNow().UtcDateTime);
        if (refusal is null)
        {
            return Result.Success;
        }

        _logger.LogWarning(
            "Refused removing {PermissionCode} from role {RoleId} by {ActorId}: rule {Rule}, {Reason}",
            request.PermissionCode, request.RoleId, request.ActorId, refusal.Rule, refusal.Reason);

        return refusal.Error;
    }

    /// <summary>
    /// The three rules, on what was read: null when the action is allowed, else the
    /// first rule that refuses it. Every entry point on an account decides here, and
    /// nowhere else.
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

        if (IsSubjectToRuleThree(request.Action)
            && LeavesNoFullAdministrator(
                fullAdministrators,
                grant => grant.UserId == request.AccountId && IsRemovedBy(request, grant),
                nowUtc))
        {
            return new Refusal(3, "no full administrator who can sign in would remain", LastFullAdministratorError(request.Action));
        }

        return null;
    }

    /// <summary>
    /// The second and third rules for removing a permission from a role, on what was
    /// read: null when the removal is allowed, else the first that refuses it. Every
    /// entry point on a role decides here, and nowhere else; the third rule's count is
    /// the one the account actions use.
    /// </summary>
    internal static Refusal? DecideRoleRemoval(
        RolePermissionRemovalRequest request,
        IReadOnlyCollection<string> actorPermissions,
        IReadOnlyList<FullAdministratorGrant> fullAdministrators,
        DateTime nowUtc)
    {
        if (!PermissionGrantGuard.Covers(actorPermissions, [request.PermissionCode]))
        {
            return new Refusal(2, "the actor does not hold the permission removed", PermissionErrors.CannotRemoveHigherPermission);
        }

        // Only * on a platform role is counted: an application role's grants never
        // reach the count, so removing a mismatched * from one takes nothing.
        var takesTheWildcard = request.RoleApplicationId is null
            && string.Equals(request.PermissionCode, Wildcard, StringComparison.Ordinal);
        if (takesTheWildcard
            && LeavesNoFullAdministrator(fullAdministrators, grant => grant.RoleId == request.RoleId, nowUtc))
        {
            return new Refusal(3, "no full administrator who can sign in would remain", UserErrors.LastFullAdministrator);
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
    /// The third rule's count, for every change it applies to: whether a change that
    /// takes away the grants <paramref name="takesAway"/> picks would leave no full
    /// administrator. It does when some account counts now and none would after. An
    /// account action takes away the account's own grants (all of them, when it locks,
    /// deactivates or deletes the account); a removal from a role takes that role's
    /// grant from every holder. A change that takes no counted grant away is never
    /// refused, whoever else is left: an account that does not count cannot be the last.
    /// </summary>
    private static bool LeavesNoFullAdministrator(
        IReadOnlyList<FullAdministratorGrant> fullAdministrators,
        Func<FullAdministratorGrant, bool> takesAway,
        DateTime nowUtc)
    {
        var counted = fullAdministrators.Where(grant => Counts(grant, nowUtc)).ToList();
        return counted.Count > 0 && counted.All(takesAway);
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
    /// Whether this way of holding <c>*</c> makes its account a full administrator: the
    /// account could sign in now and has a way to authenticate, and the grant has no
    /// end date (see the remarks on <see cref="AccountActionPolicy"/>).
    /// </summary>
    private static bool Counts(FullAdministratorGrant grant, DateTime nowUtc) =>
        CanSignIn(grant, nowUtc)
        && (grant.HasPassword || grant.HasExternalLogin)
        && grant.ExpiresAt is null;

    /// <summary>
    /// Whether the account could sign in now, by the sign-in path's own checks (see
    /// the remarks on <see cref="AccountActionPolicy"/>).
    /// </summary>
    private static bool CanSignIn(FullAdministratorGrant grant, DateTime nowUtc) =>
        !grant.IsDeleted
        && grant.EmailConfirmed
        && !AuthenticationHelper.CheckAccountStatus(grant.LockState.Status).IsError
        && grant.LockState.Kind(nowUtc) != AccountLockKind.Admin;

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
