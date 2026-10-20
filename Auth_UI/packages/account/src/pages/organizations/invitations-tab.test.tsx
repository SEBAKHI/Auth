import { existsSync, readFileSync } from "node:fs"
import path from "node:path"

import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, within } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n, { applyLanguage } from "@authsystem/i18n"
import { __resetDataTableStorage } from "@authsystem/ui/data-table/storage"

import {
  DEFAULT_INVITATION_STATUSES,
  INVITATION_STATUS_VALUES,
  INVITATION_STATUSES,
  invitationStatusParams,
  invitationStatusUrlFilter,
} from "./invitation-list"

/**
 * OI-193: an organization's invitations tab pages, sorts and filters on the
 * server, shows each status in the page's language, and offers Resend only
 * where the server says it works. The API is answered at the typed client, so
 * what the tab ASKED for is asserted next to what it RENDERED.
 */

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => mocks.get(...args),
    POST: (...args: unknown[]) => mocks.post(...args),
  },
}))

vi.mock("@authsystem/api/ui-preferences", () => ({
  fetchUiPreferences: vi.fn(async () => ({})),
  putUiPreference: vi.fn(async () => true),
  deleteUiPreference: vi.fn(async () => true),
}))

const { InvitationsTab } = await import("./organization-detail-page")

const ORG = "11111111-1111-1111-1111-111111111111"
const INVITATIONS_PATH = "/api/v1/Organizations/{id}/invitations"

const row = (overrides: Record<string, unknown>) => ({
  id: crypto.randomUUID(),
  organizationId: ORG,
  email: "someone@example.com",
  roleName: "Organization Member",
  status: "Pending",
  expiresAt: "2026-12-01T00:00:00Z",
  isExpired: false,
  canResend: true,
  createdAt: "2026-10-01T00:00:00Z",
  ...overrides,
})

const ROWS = [
  row({ email: "live@example.com" }),
  // Stored pending, past its expiry: reads Expired, and Resend still works.
  row({
    email: "lapsed@example.com",
    status: "Expired",
    isExpired: true,
    canResend: true,
  }),
  // Stored Expired: no Resend; the address can simply be invited again.
  row({
    email: "gone@example.com",
    status: "Expired",
    isExpired: true,
    canResend: false,
  }),
  row({
    email: "joined@example.com",
    status: "Accepted",
    canResend: false,
    acceptedAt: "2026-10-10T09:00:00Z",
    acceptedByUserName: "Ada Accepter",
    applicationName: "Customer Desk",
    applicationRoleName: "Sales representative",
    applicationRoleOutcome: "Dropped",
  }),
  row({ email: "nope@example.com", status: "Declined", canResend: false }),
]

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

function renderTab(search = "") {
  mocks.get.mockImplementation((requestPath: string) =>
    requestPath === INVITATIONS_PATH
      ? ok({
          invitations: ROWS,
          totalCount: 45,
          pageNumber: 1,
          pageSize: 20,
          totalPages: 3,
        })
      : ok([])
  )
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/organizations/${ORG}${search}`]}>
        <InvitationsTab orgId={ORG} />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

function listRequests() {
  return mocks.get.mock.calls
    .filter(([requestPath]) => requestPath === INVITATIONS_PATH)
    .map(([, init]) => (init as { params: { query: unknown } }).params.query)
}

function rowFor(email: string) {
  return screen.getByText(email).closest("tr") as HTMLElement
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
})

afterEach(() => {
  cleanup()
  __resetDataTableStorage()
  vi.clearAllMocks()
})

describe("the request", () => {
  it("asks the server for page one of pending and expired, newest first", async () => {
    renderTab()
    await screen.findByText("live@example.com")

    expect(listRequests()).toEqual([
      {
        status: [
          INVITATION_STATUS_VALUES.Pending,
          INVITATION_STATUS_VALUES.Expired,
        ],
        pageNumber: 1,
        pageSize: 20,
        sortBy: undefined,
        sortDirection: undefined,
      },
    ])
  })

  it("sends the page, sort and status the URL names", async () => {
    renderTab(
      "?invitations.status=Accepted,Declined&invitations.page=2&invitations.sort=acceptedAt&invitations.direction=desc"
    )
    await screen.findByText("joined@example.com")

    expect(listRequests().at(-1)).toMatchObject({
      status: [
        INVITATION_STATUS_VALUES.Accepted,
        INVITATION_STATUS_VALUES.Declined,
      ],
      pageNumber: 2,
      pageSize: 20,
      sortBy: "acceptedAt",
    })
  })

  it("asks for every status when the filter is cleared, never for none", async () => {
    renderTab("?invitations.status=all")
    await screen.findByText("live@example.com")

    expect(listRequests().at(-1)).toMatchObject({
      status: INVITATION_STATUSES.map((s) => INVITATION_STATUS_VALUES[s]),
    })
  })
})

describe("the rows", () => {
  it("offers Resend exactly where the server says it works", async () => {
    renderTab("?invitations.status=all")
    await screen.findByText("live@example.com")

    const resendable = ROWS.filter((r) => r.canResend).map((r) => r.email)
    for (const { email } of ROWS) {
      const resend = within(rowFor(email)).queryByRole("button", {
        name: "Resend",
      })
      if (resendable.includes(email)) expect(resend).not.toBeNull()
      else expect(resend, email).toBeNull()
    }
    expect(screen.getAllByRole("button", { name: "Resend" })).toHaveLength(2)
  })

  it("shows who accepted and when, and that the application role was not granted", async () => {
    renderTab("?invitations.status=all")
    await screen.findByText("joined@example.com")
    const joined = rowFor("joined@example.com")

    expect(joined).toHaveTextContent("Accepted by Ada Accepter on")
    expect(joined).toHaveTextContent("Role not granted")
    expect(joined).toHaveTextContent("You can give it from the Members tab.")
  })

  it.each(["en", "ar", "fa"])(
    "never puts the raw status word on screen (%s)",
    async (language) => {
      await applyLanguage(language as "en" | "ar" | "fa")
      renderTab("?invitations.status=all")
      await screen.findByText("live@example.com")

      const badges = Array.from(
        document.querySelectorAll('tbody [data-slot="badge"]')
      ).map((badge) => badge.textContent)
      expect(badges).toHaveLength(ROWS.length)
      const expected = ROWS.map((r) =>
        i18n.t(`organizations.invitationStatus.${r.status.toLowerCase()}`)
      )
      expect(badges).toEqual(expected)
      if (language !== "en") {
        for (const raw of INVITATION_STATUSES) {
          expect(badges).not.toContain(raw)
        }
      }
    }
  )
})

describe("the status filter in the URL", () => {
  const codec = invitationStatusUrlFilter()

  it("opens on pending and expired, written as no parameter", () => {
    expect(codec.parse(null)).toEqual([...DEFAULT_INVITATION_STATUSES])
    expect(codec.serialize(["Expired", "Pending"])).toBeNull()
  })

  it("keeps a cleared filter (every status) apart from the default", () => {
    expect(codec.serialize([])).toBe("all")
    expect(codec.parse("all")).toEqual([])
    expect(invitationStatusParams([])).toHaveLength(INVITATION_STATUSES.length)
  })

  it("drops unknown values, and falls back to the default when none is left", () => {
    expect(codec.parse("Accepted,Bogus,Accepted")).toEqual(["Accepted"])
    expect(codec.parse("Bogus")).toEqual([...DEFAULT_INVITATION_STATUSES])
  })
})

/** Walk up to the repository root, so the path holds wherever vitest is started. */
function repositoryRoot(): string {
  let directory = process.cwd()
  for (let depth = 0; depth < 10; depth += 1) {
    if (existsSync(path.join(directory, "Auth", "Auth.sln"))) return directory
    const parent = path.dirname(directory)
    if (parent === directory) break
    directory = parent
  }
  throw new Error("Auth/Auth.sln not found above " + process.cwd())
}

describe("the status numbers", () => {
  it("are the C# InvitationStatus values", () => {
    const source = readFileSync(
      path.join(
        repositoryRoot(),
        "Auth/Auth.Domain/Entities/OrganizationInvitation.cs"
      ),
      "utf8"
    )
    const body = source.match(/public enum InvitationStatus\s*\{([\s\S]*?)\}/)
    expect(body, "InvitationStatus not found").not.toBeNull()
    const declared = Object.fromEntries(
      Array.from(body![1].matchAll(/(\w+)\s*=\s*(\d+)/g)).map(
        ([, name, value]) => [name, Number(value)]
      )
    )
    expect(Object.keys(declared)).toHaveLength(5)
    expect(declared).toEqual(INVITATION_STATUS_VALUES)
  })
})
