import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const { post, completion } = vi.hoisted(() => ({
  post: vi.fn(),
  completion: {
    returnTo: null as string | null,
    complete: vi.fn(),
    challenge: vi.fn(),
    interstitial: vi.fn(),
  },
}))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => post(...args) },
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ completeEmailVerification: vi.fn() }),
}))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => completion,
}))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    children,
    footer,
  }: {
    title: string
    children: React.ReactNode
    footer?: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {children}
      {footer}
    </div>
  ),
}))

import { VerifyEmailPage } from "./verify-email-page"

const AUTHORIZE =
  "https://api.example.com/api/v1/auth/authorize?client_id=app&state=xyz"

function Landing() {
  const location = useLocation()
  return <div data-testid="landing">{location.pathname}</div>
}

function renderWith(state: unknown) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[{ pathname: "/verify-email", state }]}>
        <Routes>
          <Route path="/verify-email" element={<VerifyEmailPage />} />
          <Route path="*" element={<Landing />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

describe("VerifyEmailPage", () => {
  beforeEach(() => {
    post.mockReset()
    post.mockResolvedValue({ data: { maskedEmail: "j***@one.example" } })
    completion.returnTo = null
  })

  it("always offers the way back to sign-in, carrying a pending request", () => {
    // The server answers a confirmed address exactly as it answers an unknown
    // one, so no error will ever arrive to route this person anywhere: the
    // link has to be there before anything fails.
    completion.returnTo = AUTHORIZE
    renderWith({
      email: "jane@one.example",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    })

    expect(screen.getByRole("link", { name: /back to sign in/i })).toHaveAttribute(
      "href",
      `/login?returnTo=${encodeURIComponent(AUTHORIZE)}`
    )
  })

  it("links plainly to sign-in when no request is pending", () => {
    renderWith({
      email: "jane@one.example",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    })

    expect(screen.getByRole("link", { name: /back to sign in/i })).toHaveAttribute(
      "href",
      "/login"
    )
  })

  it("requests a code on mount only when it was not handed a live one", async () => {
    const live = renderWith({
      email: "jane@one.example",
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    })
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(post).not.toHaveBeenCalled()
    live.unmount()

    renderWith({ email: "jane@one.example" })
    await waitFor(() =>
      expect(post).toHaveBeenCalledWith(
        "/api/v1/Auth/resend-verification-email",
        { body: { email: "jane@one.example" } }
      )
    )
  })

  it("falls back to sign-in without an address to verify", () => {
    renderWith(null)

    expect(screen.getByTestId("landing")).toHaveTextContent("/login")
  })
})
