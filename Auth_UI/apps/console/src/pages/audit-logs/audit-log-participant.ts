/**
 * Which side of an audit row a person has to be on for a filter to match them.
 *
 * The console's mirror of `Auth.Domain.Enums.AuditParticipantRole`, kept here
 * rather than inlined at the one screen that offers the choice: the API takes
 * the enum's ORDINAL, so the mapping below is a wire contract and not a display
 * detail. `audit-log-participant.test.ts` reads the C# file and fails when the
 * two drift — reordering the enum there would otherwise change what every saved
 * link means, silently and in the right direction to be believed.
 */

/** The roles, in the order the console offers them. */
export const AUDIT_PARTICIPANT_ROLES = ["either", "subject", "actor"] as const

export type AuditParticipantRole = (typeof AUDIT_PARTICIPANT_ROLES)[number]

/**
 * The value the API expects, which is the C# enum's ordinal — the same way
 * `sortDirection` travels. The keys are the URL tokens, because a role in a
 * shared link should say what it means.
 */
const ROLE_PARAM: Record<AuditParticipantRole, number> = {
  subject: 0,
  actor: 1,
  either: 2,
}

export function participantRoleParam(role: AuditParticipantRole): number {
  return ROLE_PARAM[role]
}

/**
 * The day the actor started being recorded as itself.
 *
 * Until commit `6ebd52e` the insert wrote `PerformedBy = UserId`, discarding
 * whatever the caller passed — so on every older row the two sides name the
 * same person. An actor filter over that data returns what was done TO someone
 * labelled as what they did, and an "either" filter moves nothing at all. The
 * data cannot be repaired: the actor was never written down.
 *
 * A date, not a feature flag: it is a fact about rows already on disk, and it
 * stops being relevant only when retention finally passes it — 1095 days at the
 * configured floor.
 */
export const ACTOR_RECORDED_FROM = "2026-08-24"
