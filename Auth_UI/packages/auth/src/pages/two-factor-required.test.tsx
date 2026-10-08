import type * as React from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { MfaRequirement } from "@authsystem/api/mfa-requirement"

/**
 * The page between a platform administrator and the console (S08): it starts by
 * refreshing (another tab may have stepped up), shows the one step the server
 * names, and ends every step the same way — a refreshed token, the account read
 * again, and the completion every sign-in uses.
 */
const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  refreshSessionNow: vi.fn(),
  complete: vi.fn(),
  calls: [] as string[],
  auth: {
    user: { id: "u-1", email: "admin@example.com" },
    mfaRequirement: "step_up" as MfaRequirement,
    refreshUser: vi.fn(),
    logout: vi.fn(),
  },
}))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: mocks.post, GET: vi.fn() },
  refreshSessionNow: mocks.refreshSessionNow,
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }))

// The real layout carries the language and theme toggles, which need their
// providers; the page's own content is what is under test.
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

vi.mock("../auth-context", () => ({ useAuth: () => mocks.auth }))

vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({ complete: mocks.complete }),
}))

import { TwoFactorRequiredPage } from "./two-factor-required"

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <TwoFactorRequiredPage />
    </QueryClientProvider>
  )
}

beforeEach(() => {
  mocks.calls.length = 0
  mocks.post.mockReset()
  mocks.complete.mockReset()
  mocks.auth.logout.mockReset()
  mocks.auth.mfaRequirement = "step_up"
  mocks.refreshSessionNow.mockReset().mockImplementation(async () => {
    mocks.calls.push("refresh")
    return true
  })
  mocks.auth.refreshUser.mockReset().mockImplementation(async () => {
    mocks.calls.push("me")
  })
})

describe("TwoFactorRequiredPage", () => {
  it("starts with a refresh, then the account, before showing a step", async () => {
    renderPage()

    expect(await screen.findByLabelText("auth.twoFactorCode")).toBeInTheDocument()
    expect(mocks.calls).toEqual(["refresh", "me"])
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("completes straight away when another tab already stepped up", async () => {
    mocks.auth.mfaRequirement = "none"
    renderPage()

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledWith({}))
    expect(mocks.calls).toEqual(["refresh", "me"])
  })

  it("steps up with the code, then refreshes and reads the account again", async () => {
    mocks.post.mockResolvedValue({ data: undefined, error: undefined })
    const user = userEvent.setup()
    renderPage()

    await user.type(await screen.findByLabelText("auth.twoFactorCode"), "123456")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    await waitFor(() => expect(mocks.calls).toEqual(["refresh", "me", "refresh", "me"]))
    expect(mocks.post).toHaveBeenCalledWith("/api/v1/auth/2fa/step-up", {
      body: { code: "123456", useRecoveryCode: false },
    })
  })

  it("steps up with a recovery code", async () => {
    mocks.post.mockResolvedValue({ data: undefined, error: undefined })
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "auth.useRecoveryCode" }))
    await user.type(screen.getByLabelText("auth.recoveryCode"), "ABCD-EFGH")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    await waitFor(() =>
      expect(mocks.post).toHaveBeenCalledWith("/api/v1/auth/2fa/step-up", {
        body: { code: "ABCD-EFGH", useRecoveryCode: true },
      })
    )
  })

  it("asks to sign in again when the session cannot be upgraded", async () => {
    mocks.post.mockResolvedValue({
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    })
    const user = userEvent.setup()
    renderPage()

    await user.type(await screen.findByLabelText("auth.twoFactorCode"), "123456")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    expect(await screen.findByRole("alertdialog", { name: "auth.reauthenticateTitle" })).toBeInTheDocument()
    expect(screen.getByText("auth.mfaRequiredReauthenticateBody")).toBeInTheDocument()
    expect(mocks.calls).toEqual(["refresh", "me"])
  })

  it("offers enrolment when the account has no factor", async () => {
    mocks.auth.mfaRequirement = "enroll"
    renderPage()

    expect(await screen.findByText("auth.mfaRequiredEnroll")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "profile.enableTwoFactor" })).toBeInTheDocument()
  })

  it("offers to sign in again when the session's first factor is unknown", async () => {
    mocks.auth.mfaRequirement = "reauthenticate"
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "auth.reauthenticateAction" }))

    expect(await screen.findByRole("alertdialog", { name: "auth.reauthenticateTitle" })).toBeInTheDocument()
  })

  it("always offers a way out", async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole("button", { name: "common.signOut" }))

    expect(mocks.auth.logout).toHaveBeenCalled()
  })
})
