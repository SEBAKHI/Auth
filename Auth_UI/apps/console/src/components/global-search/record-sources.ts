import {
  AppWindow,
  Building2,
  KeyRound,
  Layout,
  Mail,
  ShieldCheck,
  Users,
  type LucideIcon,
} from "lucide-react"

import { api } from "@authsystem/api/client"
import { toNumber, unwrap } from "@authsystem/api/helpers"
import { fullName } from "@authsystem/ui/format"

import { PERMISSIONS } from "@/lib/constants"

/**
 * One record on the platform — a user, a role, an application — as a row.
 *
 * Records are the half of "search the platform" that no static index can hold:
 * they are created after the console ships, they number in the thousands, and
 * they are the thing an admin is actually looking for when they type a name.
 */
export interface RecordHit {
  /** Unique across sources, so `recent` can key on it. */
  id: string
  title: string
  /** The line under the title: whatever tells two same-named records apart. */
  description: string
  route: string
}

/**
 * What one source returns for one query.
 *
 * `totalCount` comes from the server and is how a group can say "5 of 137"
 * instead of showing five rows and letting them read as all of them. A `local`
 * source leaves it undefined: nothing was truncated server-side, so the count
 * is whatever the in-memory filter matched.
 */
export interface RecordPage {
  hits: RecordHit[]
  totalCount?: number
}

/**
 * How a record source is queried.
 *
 * `remote` endpoints take the search term themselves, so every keystroke (after
 * the debounce) is a request and the query is part of the cache key. `local`
 * endpoints have no search parameter but return a small, bounded list — roles,
 * permissions and layouts are per-application configuration, not user data — so
 * the list is fetched once and filtered in memory. Filtering a *paged* endpoint
 * client-side would be a lie: it would search the first page and call it the
 * platform.
 */
export type RecordSourceMode = "remote" | "local"

export interface RecordSource {
  /** Stable id: prefixes every hit's id and identifies the group. */
  key: string
  /**
   * Heading for the group. Reuses the navigation label the section already has
   * rather than a second set of translations that could drift from it.
   */
  headingKey: string
  icon: LucideIcon
  /**
   * What the viewer must hold. The API is the authoritative check — this only
   * stops the console from issuing a request that is going to 403, and from
   * showing an empty group that implies "there are none" when the truth is
   * "you may not look".
   */
  permission?: string
  /**
   * What the viewer must *not* hold. Exists for the one entity that is reached
   * two different ways: a platform admin searches every organization through
   * the admin endpoint, while a plain member searches the ones they belong to.
   * Both cannot be live at once or the member's organizations would appear
   * twice for an admin who is also a member.
   */
  deniedPermission?: string
  mode: RecordSourceMode
  /** Cache key; the query is part of it only where the server does the filtering. */
  queryKey: (query: string) => unknown[]
  /**
   * Where the rest of the matches live. The palette shows the first few and
   * hands the query off here, so a capped group is a starting point rather than
   * a wall.
   */
  listRoute: string
  fetch: (input: {
    query: string
    signal: AbortSignal
    limit: number
  }) => Promise<RecordPage>
}

/**
 * How many rows one source is asked for. Five are shown; the sixth is what
 * lets the group say "5 of 6+" rather than pretending it was exhaustive.
 */
export const RECORD_FETCH_LIMIT = 6

/** Shown per group before the count takes over. */
export const MAX_RECORDS_PER_GROUP = 5

/**
 * Below this the term matches half the platform, and every source would fire a
 * request per keystroke to say so. Pages and settings are unaffected: they are
 * in memory and answer from the first character.
 */
export const MIN_RECORD_QUERY = 2

export const RECORD_SOURCES: readonly RecordSource[] = [
  {
    key: "user",
    headingKey: "nav.users",
    icon: Users,
    permission: PERMISSIONS.users.read,
    mode: "remote",
    queryKey: (query) => ["global-search", "users", query],
    listRoute: "/users",
    fetch: async ({ query, signal, limit }) => {
      const result = await unwrap(
        api.GET("/api/v1/Users", {
          params: { query: { pageNumber: 1, pageSize: limit, searchTerm: query } },
          signal,
        })
      )
      return {
        hits: (result.users ?? []).map((user) => {
          // Same precedence the users table and the detail header use, so a
          // record is called the same thing wherever it is named.
          const name = fullName(user.firstName, user.lastName)
          const title = user.displayName || name || user.email || ""
          return {
            id: `user:${user.id}`,
            title,
            // The server matches email, first name and last name — not the
            // display name. Someone found by a legal name they do not go by
            // would otherwise show a title with no visible reason for being
            // in the list, so the matched name is carried on the second line.
            description: [user.email, name && name !== title ? name : null]
              .filter(Boolean)
              .join(" · "),
            route: `/users/${user.id}`,
          }
        }),
        totalCount: toNumber(result.totalCount),
      }
    },
  },
  {
    key: "application",
    headingKey: "nav.applications",
    icon: AppWindow,
    permission: PERMISSIONS.applications.read,
    mode: "remote",
    queryKey: (query) => ["global-search", "applications", query],
    listRoute: "/applications",
    fetch: async ({ query, signal, limit }) => {
      const result = await unwrap(
        api.GET("/api/v1/Applications", {
          params: { query: { pageNumber: 1, pageSize: limit, searchTerm: query } },
          signal,
        })
      )
      return {
        hits: (result.applications ?? []).map((application) => ({
          id: `application:${application.id}`,
          title: application.name ?? application.code ?? "",
          description: application.code ?? "",
          route: `/applications/${application.id}`,
        })),
        totalCount: toNumber(result.totalCount),
      }
    },
  },
  {
    key: "organization",
    headingKey: "nav.organizations",
    icon: Building2,
    // Searching every tenant by name is a platform-admin capability, and the
    // endpoint that does it is gated to match.
    permission: PERMISSIONS.organizations.read,
    mode: "remote",
    queryKey: (query) => ["global-search", "organizations", query],
    listRoute: "/organizations",
    fetch: async ({ query, signal, limit }) => {
      const result = await unwrap(
        api.GET("/api/v1/Organizations/all", {
          params: { query: { pageNumber: 1, pageSize: limit, searchTerm: query } },
          signal,
        })
      )
      return {
        hits: (result.organizations ?? []).map((organization) => ({
          id: `organization:${organization.id}`,
          title: organization.name ?? organization.code ?? "",
          // The three columns the server matches on, minus the one already in
          // the title.
          description: [organization.code, organization.contactEmail]
            .filter(Boolean)
            .join(" · "),
          route: `/organizations/${organization.id}`,
        })),
        totalCount: toNumber(result.totalCount),
      }
    },
  },
  {
    // The same group, for everyone else. `/organizations` has no route guard —
    // a member reaches the page and sees the organizations they belong to — so
    // gating the *search* on the platform permission would have left them able
    // to open the list but not to find anything in it.
    key: "organization-membership",
    headingKey: "nav.organizations",
    icon: Building2,
    deniedPermission: PERMISSIONS.organizations.read,
    // Membership-scoped, unpaged and as small as the number of organizations
    // one person belongs to.
    mode: "local",
    queryKey: () => ["global-search", "organizations", "membership"],
    listRoute: "/organizations",
    fetch: async ({ signal }) => {
      const organizations = await unwrap(
        api.GET("/api/v1/Organizations", { signal })
      )
      return {
        hits: (organizations ?? []).map((organization) => ({
          id: `organization:${organization.id}`,
          title: organization.name ?? organization.code ?? "",
          description: organization.code ?? "",
          route: `/organizations/${organization.id}`,
        })),
      }
    },
  },
  {
    key: "role",
    headingKey: "nav.roles",
    icon: ShieldCheck,
    permission: PERMISSIONS.roles.read,
    // Unpaged and application-scoped configuration, not a data set.
    mode: "local",
    queryKey: () => ["global-search", "roles"],
    listRoute: "/roles",
    fetch: async ({ signal }) => {
      const roles = await unwrap(api.GET("/api/v1/Roles", { signal }))
      return {
        hits: (roles ?? []).map((role) => ({
          id: `role:${role.id}`,
          title: role.name ?? role.code ?? "",
          // The application is what tells two roles named "Admin" apart.
          description: [role.code, role.applicationName]
            .filter(Boolean)
            .join(" · "),
          route: `/roles/${role.id}`,
        })),
      }
    },
  },
  {
    key: "permission",
    headingKey: "nav.permissions",
    icon: KeyRound,
    permission: PERMISSIONS.permissions.read,
    mode: "local",
    queryKey: () => ["global-search", "permissions"],
    listRoute: "/permissions",
    fetch: async ({ signal }) => {
      const permissions = await unwrap(api.GET("/api/v1/Permissions", { signal }))
      return {
        hits: (permissions ?? []).map((permission) => ({
          id: `permission:${permission.id}`,
          title: permission.name ?? permission.code ?? "",
          description: [permission.code, permission.applicationName]
            .filter(Boolean)
            .join(" · "),
          route: `/permissions/${permission.id}`,
        })),
      }
    },
  },
  {
    key: "notification-template",
    headingKey: "nav.notificationTemplates",
    icon: Mail,
    permission: PERMISSIONS.notificationTemplates.read,
    mode: "remote",
    queryKey: (query) => ["global-search", "notification-templates", query],
    listRoute: "/notifications/templates",
    fetch: async ({ query, signal, limit }) => {
      const result = await unwrap(
        api.GET("/api/v1/notification-templates", {
          params: { query: { pageNumber: 1, pageSize: limit, searchTerm: query } },
          signal,
        })
      )
      return {
        hits: (result.templates ?? []).map((template) => ({
          id: `notification-template:${template.id}`,
          title: template.typeName ?? template.typeCode ?? "",
          // The type code is one of the columns the server matches on, so it
          // is shown rather than left to explain a hit that looks unexplained.
          description: [template.typeCode, template.channel, template.applicationName]
            .filter(Boolean)
            .join(" · "),
          route: `/notifications/templates/${template.id}`,
        })),
        totalCount: toNumber(result.totalCount),
      }
    },
  },
  {
    key: "notification-layout",
    headingKey: "nav.notificationLayouts",
    icon: Layout,
    // Reading layouts is gated on the templates permission; the layouts one
    // gates writing them, and using it here would hide the list from admins
    // who may read it.
    permission: PERMISSIONS.notificationTemplates.read,
    mode: "local",
    queryKey: () => ["global-search", "notification-layouts"],
    listRoute: "/notifications/layouts",
    fetch: async ({ signal }) => {
      const layouts = await unwrap(
        api.GET("/api/v1/notification-layouts", { signal })
      )
      return {
        hits: (layouts ?? []).map((layout) => ({
          id: `notification-layout:${layout.id}`,
          title: layout.name ?? "",
          description: [layout.channel, layout.applicationName]
            .filter(Boolean)
            .join(" · "),
          route: `/notifications/layouts/${layout.id}`,
        })),
      }
    },
  },
]

/**
 * The icon for a record, recovered from its id.
 *
 * Every id is `<sourceKey>:<guid>`, so a remembered row can be drawn with the
 * right marker without storing the icon alongside it — or re-querying the
 * source it came from just to find out what shape to draw.
 */
export function recordIcon(id: string): LucideIcon | undefined {
  const sourceKey = id.slice(0, id.indexOf(":"))
  return RECORD_SOURCES.find((source) => source.key === sourceKey)?.icon
}

/**
 * Deliberately absent, so the next person does not have to re-derive it:
 *
 * - **API keys and webhook keys** have no platform-wide list. Both endpoints
 *   require an `applicationId`, so "search every key" would mean fanning out one
 *   request per application — and the value a hit could show is a prefix, not
 *   something anyone types from memory.
 * - **Audit log entries** have no free-text search. The endpoint filters by
 *   actor, action and date range; a palette that matched none of those would
 *   return nothing and read as broken.
 * - **Outbox messages** are searchable, but a message has no page to land on —
 *   it opens in a sheet inside the delivery log — and its searchable text is
 *   recipient addresses. The log itself is indexed as a page instead.
 */
export const EXCLUDED_RECORD_SOURCES = [
  "api-keys",
  "webhook-keys",
  "audit-logs",
  "notification-outbox",
] as const
