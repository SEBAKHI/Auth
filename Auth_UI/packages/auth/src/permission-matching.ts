/**
 * Decides whether a set of held permission codes satisfies a required one,
 * using the same semantics the API enforces.
 *
 * This is a port, not an invention. The rule lives in four C# copies —
 * `PermissionRequirementHandler.PermissionMatches`, `PermissionChecker`,
 * `PermissionCode.Matches`, and a fourth in `Auth.Sdk/Authorization` for
 * downstream applications, which reads three claim types instead of one and has
 * no organization branch. Nothing holds the four together; this comment is the
 * only place that says they exist. The frontend used to implement two thirds of
 * the rule: `"*"` and an exact string match, but not the prefix wildcard.
 *
 * That gap is invisible while nobody holds a prefix wildcard, and becomes a
 * lockout the moment someone does. A holder of `users:*` passes every API call
 * and is refused by every UI gate keyed on `users:read` — the console renders
 * empty for an account the server considers fully authorised. The seeded
 * `org-admin` role already holds `org:members:*`, `org:apps:*` and
 * `org:permissions:*`, so this is live today for organisation screens; seeding
 * the platform roles would extend it to the whole console.
 *
 * Note what the rule is NOT: it is a left-anchored string prefix, not a
 * segment-aware tree walk and not a lookup through `Permissions.ParentId`.
 * `auth:users:*` therefore does not satisfy `users:read`, and no amount of
 * hierarchy in the database changes that.
 */
export function permissionMatches(
  held: readonly string[],
  required: string
): boolean {
  return held.some((code) => codeMatches(code, required))
}

function codeMatches(held: string, required: string): boolean {
  if (held === "*") return true

  if (held.toLowerCase() === required.toLowerCase()) return true

  if (held.endsWith(":*")) {
    const prefix = held.slice(0, -2)
    return (
      required.toLowerCase().startsWith(`${prefix.toLowerCase()}:`) ||
      required.toLowerCase() === prefix.toLowerCase()
    )
  }

  return false
}
