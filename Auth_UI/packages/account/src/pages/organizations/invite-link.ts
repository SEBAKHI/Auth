/**
 * A link that opens an organization's invite dialog with its choices made, so an
 * application can send its user to "invite a colleague as X" in one click:
 *
 *   /organizations/{orgId}?invite=1&role=org-member&application=APP&applicationRole=rep
 *
 * The link only fills the dialog. It never submits, and it never carries the
 * invitee's address: the owner types that, so a link someone else crafted cannot
 * decide whom an invitation goes to. Every value is a code that is matched
 * against the lists the server returns for this caller — what the caller may
 * hand out — and a value with no match leaves its field empty.
 */

export const INVITE_LINK_PARAMS = {
  open: "invite",
  role: "role",
  application: "application",
  applicationRole: "applicationRole",
} as const

/** A role or application code: what the server stores, nothing a page renders. */
const CODE = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$/

export interface InviteLink {
  role?: string
  application?: string
  applicationRole?: string
}

function code(params: URLSearchParams, name: string): string | undefined {
  const value = params.get(name)?.trim()
  return value && CODE.test(value) ? value : undefined
}

/** The link's choices, or null when the URL does not ask to open the dialog. */
export function readInviteLink(params: URLSearchParams): InviteLink | null {
  if (params.get(INVITE_LINK_PARAMS.open) !== "1") return null

  const application = code(params, INVITE_LINK_PARAMS.application)
  return {
    role: code(params, INVITE_LINK_PARAMS.role),
    application,
    // An application role means nothing without its application.
    applicationRole: application
      ? code(params, INVITE_LINK_PARAMS.applicationRole)
      : undefined,
  }
}

/** The URL with the link's parameters removed and the invitations tab kept. */
export function withoutInviteLink(
  params: URLSearchParams,
  tabParam: string,
  tab: string
): URLSearchParams {
  const next = new URLSearchParams(params)
  for (const name of Object.values(INVITE_LINK_PARAMS)) next.delete(name)
  next.set(tabParam, tab)
  return next
}

export interface Coded {
  id?: string | null
  code?: string | null
}

interface OrganizationApplication {
  applicationId?: string | null
  applicationCode?: string | null
}

function byCode<T>(
  items: readonly T[],
  read: (item: T) => string | null | undefined,
  wanted?: string
) {
  if (!wanted) return undefined
  const lowered = wanted.toLowerCase()
  return items.find((item) => read(item)?.toLowerCase() === lowered)
}

export interface InviteChoices {
  roleId?: string
  applicationId?: string
  appRoleId?: string
}

/**
 * The link's codes as the ids the dialog selects, from the server's lists only:
 * the membership roles, the organization's enabled applications, and the roles of
 * the matched application that the caller may assign. A code the lists do not
 * hold yields nothing for its field.
 */
export function matchInviteLink(
  link: InviteLink,
  membershipRoles: readonly Coded[],
  enabledApplications: readonly OrganizationApplication[],
  assignableAppRoles: readonly Coded[]
): InviteChoices {
  const role = byCode(membershipRoles, (item) => item.code, link.role)
  const application = byCode(
    enabledApplications,
    (item) => item.applicationCode,
    link.application
  )
  const appRole = application
    ? byCode(assignableAppRoles, (item) => item.code, link.applicationRole)
    : undefined

  return {
    roleId: role?.id ?? undefined,
    applicationId: application?.applicationId ?? undefined,
    appRoleId: appRole?.id ?? undefined,
  }
}

/**
 * The lowest membership role, and the one an invitation gets unless someone
 * chooses another. Matched by its code, never by its place in the list: a list
 * that came back in another order must not make every invitation an admin's.
 * Mirrors OrganizationRoleCodes.Member on the server.
 */
export const MEMBER_ROLE_CODE = "org-member"

/**
 * The membership role an invitation starts with when nobody chose one: the
 * member role, from the server's list. A link that names a role chose one, so
 * it gets none: a code with no match leaves the field empty, as the link's
 * contract says, and the dialog shows it.
 */
export function defaultMembershipRoleId(
  link: InviteLink | null | undefined,
  membershipRoles: readonly Coded[] | undefined
): string | undefined {
  if (link?.role) return undefined
  return (
    byCode(membershipRoles ?? [], (item) => item.code, MEMBER_ROLE_CODE)?.id ??
    undefined
  )
}

/**
 * How the invite dialog shows itself:
 * - "quick": the address alone, with one sentence saying what the invitation
 *   gives, and a button that shows the choices;
 * - "full": every choice on show;
 * - "pending": the quick shape while the lists the decision needs are loading.
 *
 * Quick only when nothing is hidden that the inviter should look at: the role
 * is the member role (a link asking for org-admin shows the choices), every
 * choice a link asked for found its match (an empty field is never hidden), an
 * application has its role, and no list failed (its error shows in full).
 */
export type InviteDialogMode = "quick" | "full" | "pending"

export function inviteDialogMode(input: {
  link: InviteLink | null | undefined
  membershipRoles: readonly Coded[] | undefined
  roleId: string | undefined
  applicationId: string | undefined
  appRoleId: string | undefined
  pending: boolean
  failed: boolean
}): InviteDialogMode {
  if (input.failed) return "full"
  if (input.pending) return "pending"

  const member = byCode(
    input.membershipRoles ?? [],
    (item) => item.code,
    MEMBER_ROLE_CODE
  )
  if (!member?.id || input.roleId !== member.id) return "full"
  if (input.link?.application && !input.applicationId) return "full"
  if (input.applicationId && !input.appRoleId) return "full"
  return "quick"
}

/** The applications an invitation may name: active and enabled for the organization. */
export function enabledApplications<
  T extends { isActive?: boolean | null; applicationId?: string | null },
>(apps: readonly T[] | undefined): T[] {
  return (apps ?? []).filter((app) => app.isActive && app.applicationId)
}
