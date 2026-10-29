import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import type * as React from "react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

/**
 * The sign-out confirmation and the return to the relying party (OI-151).
 *
 * The page posts the confirmation, ends its own session, and leaves. It goes
 * back to the application, through the end-session endpoint and by the hash of
 * the registered address, only on a 204 and only when the URL named a return.
 * Every other outcome is the signed-out page, as before; Cancel is the profile.
 */

const { post, auth } = vi.hoisted(() => ({
  post: vi.fn(),
  auth: {
    status: "authenticated" as "authenticated" | "unauthenticated",
    logout: vi.fn(),
  },
}))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => post(...args) },
}))
vi.mock("@authsystem/auth/auth-context", () => ({ useAuth: () => auth }))
// The header has its own tests (flow-branding.test.tsx).
vi.mock("@authsystem/auth/flow-branding", () => ({
  useFlowBranding: () => ({
    appBranding: { name: "Partner Shop", logoUrl: null, logoUrlDark: null },
    layout: { appName: "Partner Shop", appLogoUrl: null, appLogoUrlDark: null },
  }),
}))
// The layout's chrome (branding, toggles) is not under test here.
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    children,
  }: {
    title: React.ReactNode
    children: React.ReactNode
  }) => (
    <main>
      <h1>{title}</h1>
      {children}
    </main>
  ),
}))

import { API_BASE_URL } from "@authsystem/api/env"

import { LogoutConfirmPage } from "./logout"

const HASH = "a".repeat(64)
const assign = vi.fn()

function Where() {
  const location = useLocation()
  return (
    <output data-testid="where">{location.pathname + location.search}</output>
  )
}

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/logout" element={<LogoutConfirmPage />} />
        <Route path="*" element={<Where />} />
      </Routes>
    </MemoryRouter>
  )
}

function answer(status: number) {
  post.mockResolvedValue({ response: new Response(null, { status }) })
}

beforeEach(() => {
  vi.stubGlobal("location", {
    assign,
    href: "https://accounts.example.com/logout",
  })
  auth.status = "authenticated"
  auth.logout.mockResolvedValue(undefined)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  vi.clearAllMocks()
})

async function confirm() {
  fireEvent.click(
    screen.getByRole("button", { name: "End sign-in on this browser" })
  )
  await waitFor(() => expect(assign).toHaveBeenCalledTimes(1))
  return String(assign.mock.calls[0]?.[0])
}

describe("LogoutConfirmPage", () => {
  it("confirmed with a 204 and a return named: goes back through the end-session endpoint by the hash", async () => {
    answer(204)
    renderAt(`/logout?client_id=SHOP&state=xyz&plr=${HASH}`)

    const destination = await confirm()

    expect(post).toHaveBeenCalledWith("/api/v1/Auth/end-session", {})
    expect(auth.logout).toHaveBeenCalledTimes(1)
    expect(auth.logout.mock.invocationCallOrder[0]).toBeLessThan(
      assign.mock.invocationCallOrder[0]
    )
    expect(destination).toBe(
      `${API_BASE_URL}/api/v1/auth/end-session?client_id=SHOP&state=xyz&plr=${HASH}`
    )
  })

  it("without a state: the return carries none", async () => {
    answer(204)
    renderAt(`/logout?client_id=SHOP&plr=${HASH}`)

    expect(await confirm()).toBe(
      `${API_BASE_URL}/api/v1/auth/end-session?client_id=SHOP&plr=${HASH}`
    )
  })

  it("a signed-out visitor: no own session to end, and still goes back on a 204", async () => {
    answer(204)
    auth.status = "unauthenticated"
    renderAt(`/logout?client_id=SHOP&state=xyz&plr=${HASH}`)

    expect(await confirm()).toContain("/api/v1/auth/end-session?")
    expect(auth.logout).not.toHaveBeenCalled()
  })

  for (const status of [400, 403, 429, 500]) {
    it(`confirmed but answered ${status}: the signed-out page, never the return`, async () => {
      answer(status)
      renderAt(`/logout?client_id=SHOP&state=xyz&plr=${HASH}`)

      expect(await confirm()).toBe("/signed-out?client_id=SHOP&state=xyz")
    })
  }

  it("confirmed but the request failed outright: the signed-out page, never the return", async () => {
    const reported = vi.spyOn(console, "error").mockImplementation(() => {})
    post.mockRejectedValue(new TypeError("Failed to fetch"))
    renderAt(`/logout?client_id=SHOP&state=xyz&plr=${HASH}`)

    expect(await confirm()).toBe("/signed-out?client_id=SHOP&state=xyz")
    expect(reported).toHaveBeenCalledTimes(1)
  })

  it("a 204 but ending this app's own session failed: the signed-out page, never the return", async () => {
    answer(204)
    const reported = vi.spyOn(console, "error").mockImplementation(() => {})
    auth.logout.mockRejectedValue(new Error("offline"))
    renderAt(`/logout?client_id=SHOP&state=xyz&plr=${HASH}`)

    expect(await confirm()).toBe("/signed-out?client_id=SHOP&state=xyz")
    expect(reported).toHaveBeenCalledTimes(1)
  })

  it("a 204 without a return named: the signed-out page, as before", async () => {
    answer(204)
    renderAt("/logout?client_id=SHOP&state=xyz")

    expect(await confirm()).toBe("/signed-out?client_id=SHOP&state=xyz")
  })

  for (const [label, url] of [
    [
      "a hash that is not 64 lowercase hex",
      `/logout?client_id=SHOP&state=xyz&plr=${"A".repeat(64)}`,
    ],
    [
      "a whole address in its place",
      `/logout?client_id=SHOP&state=xyz&plr=${encodeURIComponent("https://evil.example.net/")}`,
    ],
    ["no client_id", `/logout?state=xyz&plr=${HASH}`],
  ] as const) {
    it(`a 204 with ${label}: the signed-out page`, async () => {
      answer(204)
      renderAt(url)

      expect(await confirm()).toMatch(/^\/signed-out(\?|$)/)
    })
  }

  for (const url of [
    `/logout?client_id=SHOP&state=xyz&plr=${HASH}`,
    `/logout?client_id=SHOP&plr=${HASH}`,
  ]) {
    it(`Cancel goes to the profile, never the return (${url.includes("state") ? "with" : "without"} state)`, async () => {
      renderAt(url)

      // "Keep me signed in on this browser" is the Cancel of this screen.
      fireEvent.click(
        screen.getByRole("link", { name: "Keep me signed in on this browser" })
      )

      expect(await screen.findByTestId("where")).toHaveTextContent(
        /^\/profile$/
      )
      expect(post).not.toHaveBeenCalled()
      expect(assign).not.toHaveBeenCalled()
    })
  }

  it("two sign-outs in a row from the same tab, same return and no state: both go back", async () => {
    // Nothing on this side remembers an earlier sign-out: whether to go back is
    // the server's to decide, each time.
    answer(204)
    const first = renderAt(`/logout?client_id=SHOP&plr=${HASH}`)
    expect(await confirm()).toBe(
      `${API_BASE_URL}/api/v1/auth/end-session?client_id=SHOP&plr=${HASH}`
    )
    first.unmount()
    assign.mockClear()

    renderAt(`/logout?client_id=SHOP&plr=${HASH}`)
    expect(await confirm()).toBe(
      `${API_BASE_URL}/api/v1/auth/end-session?client_id=SHOP&plr=${HASH}`
    )
  })

  it("names the parameter exactly as the server reads it", () => {
    // The one manual bridge between the two sides: the server binds the hash
    // from the query parameter its PostLogoutReturnHash.Parameter names.
    let dir = process.cwd()
    let source: string | null = null
    for (let i = 0; i < 8 && source === null; i++) {
      const candidate = join(
        dir,
        "Auth/Auth.Application/Features/Authentication/EndSession/PostLogoutReturnHash.cs"
      )
      if (existsSync(candidate)) source = readFileSync(candidate, "utf8")
      dir = dirname(dir)
    }
    expect(source).not.toBeNull()
    expect(source).toMatch(/public const string Parameter = "plr";/)
    const page = readFileSync(join(__dirname, "logout.tsx"), "utf8")
    expect(page).toMatch(/const RETURN_HASH_PARAM = "plr"/)
  })
})
