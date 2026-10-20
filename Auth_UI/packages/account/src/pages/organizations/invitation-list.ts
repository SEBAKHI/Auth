import type { useTranslation } from "react-i18next"

import type { ListUrlFilter } from "@authsystem/ui/hooks/use-search-query"

type TFunction = ReturnType<typeof useTranslation>["t"]

/**
 * An organization's invitation list (OI-193): its status filter, what it asks
 * the server for, and how a row's status and application-role outcome read.
 *
 * The server owns every decision here. It computes the effective status (a
 * pending invitation past its expiry reads `Expired`), filters and pages by it,
 * and says per row whether Resend works (`canResend`). This module only maps
 * those answers to the request and to the screen.
 */

/** The effective statuses, in the order the filter offers them. */
export const INVITATION_STATUSES = [
  "Pending",
  "Expired",
  "Accepted",
  "Cancelled",
  "Declined",
] as const

export type InvitationStatusName = (typeof INVITATION_STATUSES)[number]

/**
 * The C# `InvitationStatus` values. The generated schema types the query
 * parameter as the enum's number, and the server binds a number as it binds a
 * name; `invitations-tab.test.tsx` holds this map to the C# declaration.
 */
export const INVITATION_STATUS_VALUES: Record<InvitationStatusName, number> = {
  Pending: 0,
  Accepted: 1,
  Declined: 2,
  Expired: 3,
  Cancelled: 4,
}

/** What the tab opens on: the invitations still waiting, live or expired. */
export const DEFAULT_INVITATION_STATUSES: readonly InvitationStatusName[] = [
  "Pending",
  "Expired",
]

/** The URL value for a cleared filter, which shows every status. */
const ALL_STATUSES_PARAM = "all"

const ALLOWED = new Set<string>(INVITATION_STATUSES)

function sameStatuses(
  a: readonly InvitationStatusName[],
  b: readonly InvitationStatusName[]
) {
  return a.length === b.length && a.every((status) => b.includes(status))
}

/**
 * The tab's status filter in the URL. No parameter is the default view
 * (pending and expired); a cleared filter is `all`, so the two stay distinct
 * after a reload; anything unreadable falls back to the default.
 */
export function invitationStatusUrlFilter(): ListUrlFilter<
  InvitationStatusName[]
> {
  return {
    param: "status",
    defaultValue: [...DEFAULT_INVITATION_STATUSES],
    parse: (raw) => {
      if (raw === ALL_STATUSES_PARAM) return []
      const statuses = Array.from(
        new Set(
          (raw ?? "")
            .split(",")
            .filter((value): value is InvitationStatusName =>
              ALLOWED.has(value)
            )
        )
      )
      return statuses.length > 0 ? statuses : [...DEFAULT_INVITATION_STATUSES]
    },
    serialize: (statuses) => {
      const known = statuses.filter((status) => ALLOWED.has(status))
      if (known.length === 0) return ALL_STATUSES_PARAM
      return sameStatuses(known, DEFAULT_INVITATION_STATUSES)
        ? null
        : known.join(",")
    },
  }
}

/**
 * The `status` values to send. A cleared filter sends every status:
 * sending none would mean the server's historical default (stored-pending
 * rows only), not "all".
 */
export function invitationStatusParams(
  statuses: readonly InvitationStatusName[]
): number[] {
  const chosen = statuses.length > 0 ? statuses : INVITATION_STATUSES
  return chosen.map((status) => INVITATION_STATUS_VALUES[status])
}

type BadgeVariant = "default" | "secondary" | "destructive" | "outline"

const STATUS_BADGES: Record<InvitationStatusName, BadgeVariant> = {
  Pending: "secondary",
  Expired: "destructive",
  Accepted: "default",
  Cancelled: "outline",
  Declined: "outline",
}

const STATUS_KEYS: Record<InvitationStatusName, string> = {
  Pending: "organizations.invitationStatus.pending",
  Expired: "organizations.invitationStatus.expired",
  Accepted: "organizations.invitationStatus.accepted",
  Cancelled: "organizations.invitationStatus.cancelled",
  Declined: "organizations.invitationStatus.declined",
}

function isStatusName(value: unknown): value is InvitationStatusName {
  return typeof value === "string" && ALLOWED.has(value)
}

/**
 * A row's status as the badge shows it: the label in the active language and
 * its variant. A status this build does not know reads as a plain outline
 * badge with a dash, never as the raw enum word.
 */
export function invitationStatusBadge(
  status: unknown,
  t: TFunction
): { label: string; variant: BadgeVariant } {
  if (!isStatusName(status)) return { label: "—", variant: "outline" }
  return { label: t(STATUS_KEYS[status]), variant: STATUS_BADGES[status] }
}

const OUTCOME_KEYS: Record<string, string> = {
  Applied: "organizations.invitationOutcome.applied",
  AlreadyHeld: "organizations.invitationOutcome.alreadyHeld",
  Dropped: "organizations.invitationOutcome.dropped",
}

/**
 * What acceptance did with the application role, as a short label, or null
 * when there is no outcome (not accepted, or membership only). The wire
 * carries the member's name; the generated schema types the enum as a number,
 * so the value is compared as what it is.
 */
export function invitationOutcomeLabel(
  outcome: unknown,
  t: TFunction
): string | null {
  const key = typeof outcome === "string" ? OUTCOME_KEYS[outcome] : undefined
  return key ? t(key) : null
}

/** The application role was not given: the person joined without it. */
export function wasOutcomeDropped(outcome: unknown): boolean {
  return outcome === "Dropped"
}
