import { beforeEach, describe, expect, it, vi } from "vitest"

const get = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => get(...args) },
}))

import {
  EXCLUDED_RECORD_SOURCES,
  RECORD_FETCH_LIMIT,
  RECORD_SOURCES,
  recordIcon,
} from "./record-sources"

const signal = new AbortController().signal
const source = (key: string) => {
  const result = RECORD_SOURCES.find((item) => item.key === key)
  if (!result) throw new Error(`Missing source ${key}`)
  return result
}

async function fetchSource(key: string, data: unknown) {
  get.mockResolvedValueOnce({ data })
  return source(key).fetch({ query: "ali", signal, limit: RECORD_FETCH_LIMIT })
}

describe("record sources", () => {
  beforeEach(() => get.mockReset())

  it("maps remote people, applications and organizations to destinations", async () => {
    const users = await fetchSource("user", {
      users: [
        {
          id: "u1",
          firstName: "Alice",
          lastName: "Example",
          displayName: "Operator",
          email: "alice@example.test",
        },
      ],
      totalCount: "7",
    })
    const applications = await fetchSource("application", {
      applications: [{ id: "a1", name: null, code: "portal" }],
      totalCount: 1,
    })
    const organizations = await fetchSource("organization", {
      organizations: [
        {
          id: "o1",
          name: "Astoom",
          code: "AST",
          contactEmail: "ops@example.test",
        },
      ],
      totalCount: 1,
    })

    expect(users).toMatchObject({
      totalCount: 7,
      hits: [
        {
          id: "user:u1",
          title: "Operator",
          description: "alice@example.test · Alice Example",
          route: "/users/u1",
        },
      ],
    })
    expect(applications.hits[0]).toMatchObject({
      title: "portal",
      route: "/applications/a1",
    })
    expect(organizations.hits[0].description).toBe(
      "AST · ops@example.test"
    )
  })

  it("maps membership, role and permission lists without inventing totals", async () => {
    const memberships = await fetchSource("organization-membership", [
      { id: "o1", name: null, code: "MEMBER" },
    ])
    const roles = await fetchSource("role", [
      { id: "r1", name: "Admin", code: "admin", applicationName: "Portal" },
    ])
    const permissions = await fetchSource("permission", [
      { id: "p1", name: null, code: "users:read", applicationName: "Portal" },
    ])

    expect(memberships.hits[0]).toMatchObject({
      id: "organization:o1",
      title: "MEMBER",
    })
    expect(roles.hits[0].description).toBe("admin · Portal")
    expect(permissions.hits[0]).toMatchObject({
      title: "users:read",
      route: "/permissions/p1",
    })
    expect(memberships.totalCount).toBeUndefined()
  })

  it("maps notification templates and layouts", async () => {
    const templates = await fetchSource("notification-template", {
      templates: [
        {
          id: "t1",
          typeName: null,
          typeCode: "Welcome",
          channel: "Email",
          applicationName: "Portal",
        },
      ],
      totalCount: 4,
    })
    const layouts = await fetchSource("notification-layout", [
      { id: "l1", name: "Default", channel: "Email", applicationName: null },
    ])

    expect(templates.hits[0]).toMatchObject({
      title: "Welcome",
      description: "Welcome · Email · Portal",
      route: "/notifications/templates/t1",
    })
    expect(layouts.hits[0]).toMatchObject({
      description: "Email",
      route: "/notifications/layouts/l1",
    })
  })

  it("keeps cache keys, icons and deliberate exclusions explicit", () => {
    expect(source("user").queryKey("alice")).toEqual([
      "global-search",
      "users",
      "alice",
    ])
    expect(source("role").queryKey("ignored")).toEqual([
      "global-search",
      "roles",
    ])
    expect(recordIcon("user:1")).toBe(source("user").icon)
    expect(recordIcon("unknown:1")).toBeUndefined()
    expect(EXCLUDED_RECORD_SOURCES).toEqual([
      "api-keys",
      "webhook-keys",
      "audit-logs",
      "notification-outbox",
    ])
  })
})
