/** Default server-side page size for paged list endpoints (API caps at 100). */
export const DEFAULT_PAGE_SIZE = 20

/**
 * The shortest password the server can ever be configured to accept
 * (`Password:MinimumLength` bottoms out here, at the System Settings
 * registry's floor).
 *
 * It is a floor, NOT the live policy. Forms learn the live policy from
 * `usePasswordPolicy` (GET /Platform/password-policy) and show it as the person
 * types; this number only backs FALLBACK_PASSWORD_POLICY, the rule set enforced
 * while that request has not answered or has failed. Do not "helpfully" raise
 * it to 8: a fallback stricter than the real policy refuses passwords the
 * server accepts the moment the policy cannot be fetched.
 */
export const PASSWORD_LENGTH_FLOOR = 6
