import { routePath } from "@authsystem/ui/route-path"

/**
 * The one place a console record id becomes a URL.
 *
 * Every list, embedded table and summary card that points at a record builds
 * its `href` from here, so a record type's path is written once and its id is
 * encoded once. Anything that renders one of these is a `<Link>`: the result is
 * an address a person can copy, bookmark, or open in a second tab. Commands -
 * edit, delete, open a dialog - stay buttons.
 *
 * Each builder takes the id exactly as the generated API types hand it over -
 * optional, because the OpenAPI document marks no property `required` - and
 * answers `undefined` when there is none. That is what stops a missing id from
 * becoming a link to `/users/undefined` that only fails after the click; the
 * caller has to decide what a row with no destination looks like.
 *
 * Destinations that carry no id (`/users`, `/notifications/templates`) are
 * plain string literals at their call sites: nothing to encode, nothing to get
 * wrong.
 */

const builder =
  (path: (id: string) => string) =>
  (id: string | undefined): string | undefined =>
    id ? path(id) : undefined

export const userHref = builder((id) => routePath`/users/${id}`)

export const roleHref = builder((id) => routePath`/roles/${id}`)

export const permissionHref = builder((id) => routePath`/permissions/${id}`)

export const applicationHref = builder((id) => routePath`/applications/${id}`)

export const organizationHref = builder((id) => routePath`/organizations/${id}`)

export const notificationTemplateHref = builder(
  (id) => routePath`/notifications/templates/${id}`
)

export const notificationLayoutHref = builder(
  (id) => routePath`/notifications/layouts/${id}`
)

export const policyRevisionHref = builder(
  (id) => routePath`/notifications/policy/${id}`
)

/** Whose sign-in page designs: the platform's, or one application's. */
type LoginDesignOwner =
  | { kind: "platform" }
  | { kind: "application"; applicationId: string }

/**
 * A sign-in page design's own page. The platform's live under platform
 * settings, an application's under that application, each beside the list
 * (its "Templates" tab) it was opened from.
 */
export function loginDesignHref(
  owner: LoginDesignOwner,
  designId: string | undefined
): string | undefined {
  if (!designId) return undefined
  return owner.kind === "platform"
    ? routePath`/admin/platform-settings/login-designs/${designId}`
    : routePath`/applications/${owner.applicationId}/login-designs/${designId}`
}

/** The "Templates" tab that lists an owner's designs. */
export function loginDesignsListHref(owner: LoginDesignOwner): string {
  return owner.kind === "platform"
    ? "/admin/platform-settings?tab=templates"
    : `${routePath`/applications/${owner.applicationId}`}?tab=templates`
}
