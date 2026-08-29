using System.Reflection;

namespace Auth.Domain.Constants;

/// <summary>
/// Every permission code the API enforces, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Until this file existed the list lived nowhere. Fifty codes were inline
/// string literals repeated across a hundred and forty [RequirePermission]
/// sites in seventeen controllers, one more was demanded in controller bodies
/// only, and the console kept a second hand-written copy. Nothing held the two
/// halves together: a permission renamed server-side hid a console control with
/// no compile error, no failing test and no log line. Every comparable list in
/// this solution - audit actions, notification types, sort fields, the settings
/// registry - had a catalogue like this one, which is precisely why every one of
/// them could be guarded and this one could not.
/// </para>
/// <para>
/// Two guards read it. PermissionCatalogueCoverageTests holds it against the
/// codes the API actually demands, in both directions. The console's mirror
/// (Auth_UI/apps/console/src/lib/permissions.ts) is held against this file by
/// permissions.test.ts, which reads it as text.
/// </para>
/// <para>
/// UNTIL the [RequirePermission] sites reference these constants, this file is a
/// FIFTH copy of the same list - attributes, controller bodies, the SQL seed,
/// this catalogue, the console mirror - not a reduction. The migration is what
/// drops the attributes as an independent copy and takes the count to three.
/// So: DO NOT ADD A PERMISSION TO THE SYSTEM BEFORE THAT MIGRATION IS DONE. A
/// sixth code threaded through five copies by hand is how this drift began.
/// </para>
/// <para>
/// Nesting is deliberately ONE level deep, which is why an organization member
/// code is MembersRead and not Members.Read. The console's guard reads this file
/// as text and slices between "public static class" headers rather than counting
/// braces - a doc comment holding a route template in curly braces, a pattern
/// that lives one file away in PermissionRequirementHandler, silently unbalances
/// any brace counter and leaves the guard passing over an empty list. The shape
/// here is a concession to that reader, and is recorded as one.
/// </para>
/// <para>
/// This catalogue decides nothing on its own. It names what the API demands; the
/// API is what refuses. A console gate keyed on one of these codes controls what
/// is SHOWN, never what is ALLOWED.
/// </para>
/// </remarks>
public static class PermissionCodes
{
    public static class Users
    {
        public const string Read = "users:read";
        public const string Create = "users:create";
        public const string Update = "users:update";
        public const string Delete = "users:delete";
        public const string ManageRoles = "users:manage-roles";
        public const string ManagePermissions = "users:manage-permissions";

        /// <summary>
        /// Widens a listing to soft-deleted rows. Demanded in UsersController's
        /// body rather than by an attribute, because it gates a query parameter
        /// and not the endpoint.
        /// </summary>
        public const string Manage = "users:manage";
    }

    public static class Roles
    {
        public const string Read = "roles:read";
        public const string Create = "roles:create";
        public const string Update = "roles:update";
        public const string Delete = "roles:delete";
    }

    public static class Permissions
    {
        public const string Read = "permissions:read";
        public const string Create = "permissions:create";
        public const string Update = "permissions:update";
        public const string Delete = "permissions:delete";
        public const string Manage = "permissions:manage";
    }

    public static class Applications
    {
        public const string Read = "applications:read";
        public const string Create = "applications:create";
        public const string Update = "applications:update";
        public const string Delete = "applications:delete";
    }

    public static class ApiKeys
    {
        public const string Read = "apikeys:read";
        public const string Create = "apikeys:create";
        public const string Revoke = "apikeys:revoke";
        public const string Validate = "apikeys:validate";
        public const string Rotate = "apikeys:rotate";
    }

    public static class WebhookKeys
    {
        public const string Read = "webhookkeys:read";
        public const string Create = "webhookkeys:create";
        public const string Validate = "webhookkeys:validate";
        public const string Revoke = "webhookkeys:revoke";
        public const string Rotate = "webhookkeys:rotate";
    }

    public static class AuditLogs
    {
        public const string Read = "auditlogs:read";
        public const string Export = "auditlogs:export";
    }

    public static class Secrets
    {
        /// <summary>
        /// The one code separated by a DOT rather than a colon, deliberately:
        /// the wildcard rule matches a prefix up to a colon, so no grant of
        /// "secrets:*" can ever reach it and it must be granted by name.
        /// </summary>
        /// <remarks>
        /// The dot also makes it the one code PermissionCode's format regex
        /// rejects, since that pattern allows no dot. Latent, not live:
        /// PermissionCode.Create has no production call site, so nothing
        /// validates it today. Choosing between widening the pattern and
        /// renaming the code is a separate change - renaming touches thirteen
        /// attribute sites, a seeded row, and every grant already made.
        /// </remarks>
        public const string Manage = "secrets.manage";
    }

    public static class PlatformSettings
    {
        public const string Manage = "platform-settings:manage";
    }

    public static class SystemSettings
    {
        public const string Manage = "system-settings:manage";
    }

    public static class NotificationTemplates
    {
        public const string Read = "notification-templates:read";
        public const string Manage = "notification-templates:manage";
        public const string Publish = "notification-templates:publish";
    }

    public static class NotificationLayouts
    {
        public const string Manage = "notification-layouts:manage";
    }

    public static class PrivacyPolicy
    {
        public const string Read = "privacy-policy:read";
        public const string Manage = "privacy-policy:manage";
    }

    /// <summary>
    /// Platform-wide authority OVER organizations, held in the "permissions"
    /// claim. Not to be confused with <see cref="Org"/>, which is authority
    /// INSIDE one organization.
    /// </summary>
    public static class Organizations
    {
        public const string Read = "organizations:read";

        /// <summary>
        /// Demanded only in OrganizationsController's body, never by an
        /// attribute: it widens a member's own view to every organization
        /// rather than guarding an endpoint.
        /// </summary>
        public const string Manage = "organizations:manage";
    }

    /// <summary>
    /// Authority inside a single organization, satisfied from the "org_perm"
    /// claim - or from a live membership lookup when the token predates the
    /// membership. See PermissionRequirementHandler.
    /// </summary>
    /// <remarks>
    /// These are deliberately absent from the console's mirror. The client reads
    /// only the platform "permissions" claim, so a gate keyed on one of these
    /// would evaluate false for the organization owner who actually holds it and
    /// hide the control from the one person entitled to it. Mirroring them is
    /// blocked on the client learning to read "org_perm" - and on answering what
    /// it does when that claim is absent because the token predates the
    /// membership, which the server answers with a live lookup the client has no
    /// equivalent of.
    /// </remarks>
    public static class Org
    {
        public const string Update = "org:update";
        public const string MembersRead = "org:members:read";
        public const string MembersInvite = "org:members:invite";
        public const string MembersManage = "org:members:manage";
        public const string AppsRead = "org:apps:read";
        public const string AppsManage = "org:apps:manage";
        public const string PermissionsRead = "org:permissions:read";
        public const string PermissionsManage = "org:permissions:manage";
    }

    /// <summary>
    /// Every code above, ordinal-sorted. Built by reflection rather than
    /// restated, so a constant cannot be declared and left out of the list.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        .. typeof(PermissionCodes)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(code => code, StringComparer.Ordinal)
    ];
}
