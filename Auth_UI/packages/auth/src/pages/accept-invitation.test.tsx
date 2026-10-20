import type * as React from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * OI-192: the accept page sends the person on, and asks for nothing twice.
 *
 * (a) Signed in: Accept lands in the application when the server gives a launch
 *     address that passes the launch check, on the organization otherwise, and
 *     says so when the application role was dropped.
 * (b) Signed out: "Sign in to accept" leaves a note in this tab; back from the
 *     sign-in, the page accepts once, by itself, and never without the note, so
 *     a link alone accepts nothing.
 * (c) New account: register, then the ordinary sign-in with the password just
 *     typed, then the one completion rule, which launches the application.
 *
 * The API is answered at the typed client; the completion rule and the launch
 * check are the real ones, with only `location.assign` stubbed.
 */

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  login: vi.fn(),
  logout: vi.fn(),
  auth: {
    status: "unauthenticated" as
      "loading" | "authenticated" | "unauthenticated",
    user: null as { email: string } | null,
  },
}))

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => mocks.get(...args),
    POST: (...args: unknown[]) => mocks.post(...args),
  },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ ...mocks.auth, login: mocks.login, logout: mocks.logout }),
}))

vi.mock("sonner", () => ({
  toast: { error: vi.fn(), success: vi.fn() },
}))

vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    children,
  }: {
    title: string
    children: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {children}
    </div>
  ),
}))

const { AcceptInvitationPage, ACCEPT_AFTER_SIGN_IN_KEY } =
  await import("./accept-invitation")

const TOKEN = "aW52aXRhdGlvbi10b2tlbi1mb3ItdGhlLXRlc3Q"
const INVITATION_ID = "44444444-4444-4444-4444-444444444444"
const ORGANIZATION_ID = "55555555-5555-5555-5555-555555555555"
const LAUNCH =
  "https://crm.example.com/signin/start?iss=https%3A%2F%2Fid.example.com"
const EMAIL = "rep@example.com"
const PASSWORD = "Passw0rd!x"

const preview = (overrides: Record<string, unknown> = {}) => ({
  id: INVITATION_ID,
  organizationName: "Northwind",
  email: EMAIL,
  roleName: "Member",
  invitedByName: "Ada Admin",
  status: "Pending",
  expiresAt: "2026-12-01T00:00:00Z",
  isExpired: false,
  isAlreadyMember: false,
  userExists: true,
  applicationName: "Customer Desk",
  applicationLogoUrl: null,
  applicationLogoUrlDark: null,
  applicationRoleName: "Sales representative",
  ...overrides,
})

const accepted = (overrides: Record<string, unknown> = {}) => ({
  success: true,
  organizationId: ORGANIZATION_ID,
  organizationName: "Northwind",
  roleName: "Member",
  applicationRoleName: "Sales representative",
  applicationRoleOutcome: "Applied",
  application: {
    code: "CRM",
    name: "Customer Desk",
    logoUrl: null,
    logoUrlDark: null,
  },
  launchUrl: LAUNCH,
  ...overrides,
})

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

const assign = vi.fn()

/** Where the router ended up, and what it carried there. */
function Landing() {
  const location = useLocation()
  return (
    <div data-testid="landing">
      {location.pathname}
      {"|"}
      {JSON.stringify(location.state ?? null)}
    </div>
  )
}

function renderPage(invitation = preview()) {
  mocks.get.mockImplementation((path: string) =>
    path === "/api/v1/Platform/password-policy"
      ? ok({
          minimumLength: 8,
          requireUppercase: true,
          requireLowercase: true,
          requireDigit: true,
          requireSpecialCharacter: true,
        })
      : ok(invitation)
  )
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/accept-invitation?token=${TOKEN}`]}>
        <Routes>
          <Route path="/accept-invitation" element={<AcceptInvitationPage />} />
          <Route path="*" element={<Landing />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

function acceptCalls() {
  return mocks.post.mock.calls.filter(
    ([path]) => path === "/api/v1/Invitations/{token}/accept"
  )
}

function signedInAs(email: string) {
  mocks.auth.status = "authenticated"
  mocks.auth.user = { email }
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  assign.mockClear()
  vi.stubGlobal("location", {
    assign,
    href: "https://accounts.example.com/accept-invitation",
  })
  mocks.auth.status = "unauthenticated"
  mocks.auth.user = null
  mocks.post.mockImplementation(() => ok(accepted()))
  window.sessionStorage.clear()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe("the summary", () => {
  it("names the application and its role when the invitation carries one", async () => {
    renderPage()

    expect(await screen.findByText("Customer Desk")).toBeInTheDocument()
    expect(screen.getByText("Application")).toBeInTheDocument()
    expect(screen.getByText("Application role")).toBeInTheDocument()
    expect(screen.getByText("Sales representative")).toBeInTheDocument()
  })

  it("has no application rows for a membership-only invitation", async () => {
    renderPage(preview({ applicationName: null, applicationRoleName: null }))

    await screen.findByText("Northwind")
    expect(screen.queryByText("Application")).toBeNull()
    expect(screen.queryByText("Application role")).toBeNull()
  })
})

describe("(a) accepting while signed in", () => {
  beforeEach(() => signedInAs(EMAIL))

  it("enters the application at the server's launch address", async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(
      await screen.findByRole("button", { name: "Accept invitation" })
    )

    await waitFor(() => expect(assign).toHaveBeenCalledWith(LAUNCH))
    expect(screen.queryByTestId("landing")).toBeNull()
  })

  it.each([
    ["no launch address", null],
    ["a javascript: address", "javascript:alert(1)"],
    [
      "an http address off the loopback host",
      "http://crm.example.com/signin/start",
    ],
  ])("lands on the organization with %s", async (_, launchUrl) => {
    mocks.post.mockImplementation(() => ok(accepted({ launchUrl })))
    const user = userEvent.setup()
    renderPage()

    await user.click(
      await screen.findByRole("button", { name: "Accept invitation" })
    )

    expect(await screen.findByTestId("landing")).toHaveTextContent(
      `/organizations/${ORGANIZATION_ID}|`
    )
    expect(assign).not.toHaveBeenCalled()
  })

  it("says the application role was dropped, and leads on to the organization", async () => {
    mocks.post.mockImplementation(() =>
      ok(
        accepted({
          applicationRoleOutcome: "Dropped",
          applicationRoleName: null,
          launchUrl: null,
        })
      )
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(
      await screen.findByRole("button", { name: "Accept invitation" })
    )

    expect(
      await screen.findByTestId("invitation-role-dropped")
    ).toHaveTextContent(
      "You joined Northwind. The Customer Desk role was not given to you, because the person who invited you can no longer give it."
    )
    expect(assign).not.toHaveBeenCalled()

    await user.click(
      screen.getByRole("link", { name: "Go to the organization" })
    )
    expect(await screen.findByTestId("landing")).toHaveTextContent(
      `/organizations/${ORGANIZATION_ID}|`
    )
  })
})

describe("(b) back from the sign-in that 'Sign in to accept' started", () => {
  it("leaves a note in this tab naming the invitation, never its token", async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(
      await screen.findByRole("button", { name: "Sign in to accept" })
    )

    const note = JSON.parse(
      window.sessionStorage.getItem(ACCEPT_AFTER_SIGN_IN_KEY) ?? "null"
    )
    expect(note.invitationId).toBe(INVITATION_ID)
    expect(note.expiresAt).toBeGreaterThan(Date.now())
    expect(JSON.stringify(note)).not.toContain(TOKEN)
    expect(await screen.findByTestId("landing")).toHaveTextContent("/login|")
  })

  it("accepts once, by itself, removing the note before it sends", async () => {
    window.sessionStorage.setItem(
      ACCEPT_AFTER_SIGN_IN_KEY,
      JSON.stringify({
        invitationId: INVITATION_ID,
        expiresAt: Date.now() + 60_000,
      })
    )
    let noteWhenSent: string | null = "unset"
    mocks.post.mockImplementation(() => {
      noteWhenSent = window.sessionStorage.getItem(ACCEPT_AFTER_SIGN_IN_KEY)
      return ok(accepted())
    })
    signedInAs(EMAIL)

    renderPage()

    await waitFor(() => expect(assign).toHaveBeenCalledWith(LAUNCH))
    expect(acceptCalls()).toHaveLength(1)
    expect(noteWhenSent).toBeNull()
  })

  it("does not accept by itself without the note: a link alone accepts nothing", async () => {
    signedInAs(EMAIL)

    renderPage()

    expect(
      await screen.findByRole("button", { name: "Accept invitation" })
    ).toBeEnabled()
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(acceptCalls()).toHaveLength(0)
    expect(assign).not.toHaveBeenCalled()
  })

  it.each([
    [
      "an expired note",
      { invitationId: INVITATION_ID, expiresAt: Date.now() - 1 },
    ],
    [
      "a note for another invitation",
      { invitationId: "another", expiresAt: Date.now() + 60_000 },
    ],
  ])("does not accept by itself with %s", async (_, note) => {
    window.sessionStorage.setItem(
      ACCEPT_AFTER_SIGN_IN_KEY,
      JSON.stringify(note)
    )
    signedInAs(EMAIL)

    renderPage()

    await screen.findByRole("button", { name: "Accept invitation" })
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(acceptCalls()).toHaveLength(0)
  })

  it("does not accept for another account, and spends the note", async () => {
    window.sessionStorage.setItem(
      ACCEPT_AFTER_SIGN_IN_KEY,
      JSON.stringify({
        invitationId: INVITATION_ID,
        expiresAt: Date.now() + 60_000,
      })
    )
    signedInAs("someone-else@example.com")

    renderPage()

    expect(
      await screen.findByRole("button", { name: "Sign out" })
    ).toBeInTheDocument()
    await waitFor(() =>
      expect(window.sessionStorage.getItem(ACCEPT_AFTER_SIGN_IN_KEY)).toBeNull()
    )
    expect(acceptCalls()).toHaveLength(0)
  })
})

describe("(c) a new account", () => {
  const registered = {
    userId: "66666666-6666-6666-6666-666666666666",
    email: EMAIL,
    organizationName: "Northwind",
    roleName: "Member",
    message: "Account created and invitation accepted. Please sign in.",
    organizationId: ORGANIZATION_ID,
    applicationRoleOutcome: "Applied",
    application: { code: "CRM", name: "Customer Desk" },
    launchUrl: LAUNCH,
  }

  async function register(user: ReturnType<typeof userEvent.setup>) {
    await user.type(await screen.findByLabelText("First name"), "Rae")
    await user.type(screen.getByLabelText("Last name"), "Rep")
    await user.type(screen.getByLabelText("Password"), PASSWORD)
    await user.type(screen.getByLabelText("Confirm password"), PASSWORD)
    await user.click(
      screen.getByRole("button", { name: "Create account & join" })
    )
  }

  beforeEach(() => {
    mocks.post.mockImplementation((path: string) =>
      path === "/api/v1/Invitations/{token}/register"
        ? ok(registered)
        : ok(accepted())
    )
  })

  it("registers, signs in with the password just typed, then enters the application", async () => {
    const order: string[] = []
    mocks.post.mockImplementation((path: string) => {
      order.push(path)
      return ok(registered)
    })
    mocks.login.mockImplementation(async (...args: unknown[]) => {
      order.push(`login:${args[0]}:${args[1]}`)
      return { status: "authenticated", requiresPasswordChange: false }
    })
    const user = userEvent.setup()
    renderPage(preview({ userExists: false }))

    await register(user)

    await waitFor(() => expect(assign).toHaveBeenCalledWith(LAUNCH))
    expect(order).toEqual([
      "/api/v1/Invitations/{token}/register",
      `login:${EMAIL}:${PASSWORD}`,
    ])
  })

  it("lands on the organization when the server gives no launch address", async () => {
    mocks.post.mockImplementation(() => ok({ ...registered, launchUrl: null }))
    mocks.login.mockResolvedValue({
      status: "authenticated",
      requiresPasswordChange: false,
    })
    const user = userEvent.setup()
    renderPage(preview({ userExists: false }))

    await register(user)

    expect(await screen.findByTestId("landing")).toHaveTextContent(
      `/organizations/${ORGANIZATION_ID}|`
    )
    expect(assign).not.toHaveBeenCalled()
  })

  it("sends the person to sign in, towards the organization, when the sign-in fails", async () => {
    mocks.login.mockRejectedValue({ status: 429 })
    const user = userEvent.setup()
    renderPage(preview({ userExists: false }))

    await register(user)

    const landing = await screen.findByTestId("landing")
    expect(landing).toHaveTextContent("/login|")
    expect(landing).toHaveTextContent(`"email":"${EMAIL}"`)
    expect(landing).toHaveTextContent(
      `"from":"/organizations/${ORGANIZATION_ID}"`
    )
    expect(assign).not.toHaveBeenCalled()
  })

  it("does not sign in when the registration is refused", async () => {
    mocks.post.mockImplementation(() =>
      Promise.resolve({
        error: { status: 409, code: "User.DuplicateEmail" },
        response: new Response(null, { status: 409 }),
      })
    )
    const user = userEvent.setup()
    renderPage(preview({ userExists: false }))

    await register(user)

    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    expect(mocks.login).not.toHaveBeenCalled()
    expect(assign).not.toHaveBeenCalled()
  })
})
