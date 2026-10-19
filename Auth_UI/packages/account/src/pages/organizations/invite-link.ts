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

interface Coded {
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

/** The applications an invitation may name: active and enabled for the organization. */
export function enabledApplications<
  T extends { isActive?: boolean | null; applicationId?: string | null },
>(apps: readonly T[] | undefined): T[] {
  return (apps ?? []).filter((app) => app.isActive && app.applicationId)
}
