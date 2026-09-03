import { useQuery } from "@tanstack/react-query"

import { api } from "@authsystem/api/client"
import { PASSWORD_LENGTH_FLOOR } from "@authsystem/api/constants"
import { toNumber, unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"

/**
 * The composition rules `GET /Platform/password-policy` publishes, with the
 * minimum already coerced to a number (the schema types every int32 as
 * `number | string`). This is the whole of what the server discloses; the
 * rules it keeps to itself — common patterns, breached passwords, history —
 * arrive only as errors on submit, which `applyPasswordServerErrors` renders.
 */
export interface PasswordPolicy {
  minimumLength: number
  requireUppercase: boolean
  requireLowercase: boolean
  requireDigit: boolean
  requireSpecialCharacter: boolean
}

export const PASSWORD_POLICY_QUERY_KEY = ["password-policy"] as const

/**
 * What a form enforces while the policy is unknown — a failed fetch, a
 * gateway without the route, a browser that never got an answer: the registry
 * floor and nothing else.
 *
 * Deliberately the LOOSEST policy the server can be configured to, not the
 * shipped default. A guess stricter than the real policy refuses passwords the
 * server would accept, and a form must never be stricter than the server;
 * a guess looser than it costs one round trip, after which the server's own
 * sentences land under the field. Enforcement stays where it always was.
 */
export const FALLBACK_PASSWORD_POLICY: PasswordPolicy = {
  minimumLength: PASSWORD_LENGTH_FLOOR,
  requireUppercase: false,
  requireLowercase: false,
  requireDigit: false,
  requireSpecialCharacter: false,
}

export type PasswordRuleId =
  | "minLength"
  | "uppercase"
  | "lowercase"
  | "digit"
  | "special"

export interface PasswordRuleState {
  id: PasswordRuleId
  met: boolean
  /** The policy's minimum, carried so the minLength label can name it. */
  count?: number
}

/**
 * Mirrors of the character classes in
 * `Auth/Auth.Application/Validators/PasswordValidator.cs`, kept as pattern
 * SOURCE so `password-policy.test.ts` can read that file and compare them byte
 * for byte. ASCII on purpose, exactly like the server: an Arabic question mark
 * is not a symbol there, and a Latin-1 capital is not an uppercase letter, so
 * neither may tick a box here — a list that says "met" for a password the
 * server then refuses is worse than no list.
 */
export const PASSWORD_CHARACTER_CLASSES = {
  uppercase: "[A-Z]",
  lowercase: "[a-z]",
  digit: "[0-9]",
  special: String.raw`[!@#$%^&*()\-_=+\[\]{}|;:'",.<>?/\\]`,
} as const

const UPPERCASE = new RegExp(PASSWORD_CHARACTER_CLASSES.uppercase)
const LOWERCASE = new RegExp(PASSWORD_CHARACTER_CLASSES.lowercase)
const DIGIT = new RegExp(PASSWORD_CHARACTER_CLASSES.digit)
const SPECIAL = new RegExp(PASSWORD_CHARACTER_CLASSES.special)

/** The wire shape, coerced. */
export function normalizePasswordPolicy(
  dto: Schemas["PasswordPolicyDto"]
): PasswordPolicy {
  return {
    minimumLength: toNumber(dto.minimumLength),
    requireUppercase: dto.requireUppercase,
    requireLowercase: dto.requireLowercase,
    requireDigit: dto.requireDigit,
    requireSpecialCharacter: dto.requireSpecialCharacter,
  }
}

/**
 * Judges `value` against every rule the policy enables, in display order, so
 * the list a person reads is the list the server will apply. Length is
 * UTF-16 code units, the same count as the server's `string.Length`: a
 * surrogate-pair emoji is two on both sides.
 */
export function evaluatePassword(
  value: string,
  policy: PasswordPolicy
): PasswordRuleState[] {
  const rules: PasswordRuleState[] = [
    {
      id: "minLength",
      met: value.length >= policy.minimumLength,
      count: policy.minimumLength,
    },
  ]
  if (policy.requireUppercase) {
    rules.push({ id: "uppercase", met: UPPERCASE.test(value) })
  }
  if (policy.requireLowercase) {
    rules.push({ id: "lowercase", met: LOWERCASE.test(value) })
  }
  if (policy.requireDigit) {
    rules.push({ id: "digit", met: DIGIT.test(value) })
  }
  if (policy.requireSpecialCharacter) {
    rules.push({ id: "special", met: SPECIAL.test(value) })
  }
  return rules
}

/**
 * The live policy, shared by every password form through one cache entry.
 *
 * `policy` is undefined while the request is in flight AND after it fails;
 * callers fall back to {@link FALLBACK_PASSWORD_POLICY} for enforcement and
 * render no list, rather than a list built on a guess. Not seeded from local
 * storage the way branding is: a person acts on this value, and a stale
 * minimum shown as current is precisely the contradiction this hook exists to
 * end.
 */
export function usePasswordPolicy(): {
  policy: PasswordPolicy | undefined
  isPending: boolean
} {
  const query = useQuery({
    queryKey: PASSWORD_POLICY_QUERY_KEY,
    queryFn: async () =>
      normalizePasswordPolicy(
        await unwrap(api.GET("/api/v1/Platform/password-policy"))
      ),
    staleTime: 5 * 60 * 1000,
  })

  return { policy: query.data, isPending: query.isPending }
}
