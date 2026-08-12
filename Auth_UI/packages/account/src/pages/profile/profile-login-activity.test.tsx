import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

import { TooltipProvider } from "@authsystem/ui/tooltip"
import type { Schemas } from "@authsystem/api/types"

type Attempt = Schemas["LoginAttemptDto"]

const getMock = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => getMock(...args) },
}))

const { ProfileLoginActivity } = await import("./profile-login-activity")

/**
 * The card has three outcomes to tell apart, and getting that wrong is the
 * defect this replaced: an unfinished two-factor ceremony was rendered as a
 * failed sign-in, in red, with an untranslated English reason beside it — on
 * every successful sign-in a two-factor user made.
 */
function renderWith(...attempts: Partial<Attempt>[]) {
  getMock.mockResolvedValue({
    data: attempts.map((attempt, index) => ({
      id: `00000000-0000-0000-0000-00000000000${index}`,
      attemptedAt: new Date().toISOString(),
      isSuccess: false,
      secondFactorIncomplete: false,
      secondFactorAttempts: 0,
      failureReason: null,
      ipAddress: "203.0.113.10",
      location: null,
      deviceName: "Chrome on Windows",
      deviceType: "desktop",
      ...attempt,
    })),
    error: undefined,
  })

  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  // The card leans on Tooltip for the timestamp and location, and the app shell
  // supplies the provider in production.
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <ProfileLoginActivity />
      </TooltipProvider>
    </QueryClientProvider>
  )
}

describe("ProfileLoginActivity", () => {
  it("shows a completed sign-in as a plain success with no reason text", async () => {
    renderWith({ isSuccess: true })

    expect(await screen.findByText("Signed in")).toBeInTheDocument()
    expect(screen.queryByText("Failed sign-in")).not.toBeInTheDocument()
    expect(
      screen.queryByText("Two-factor verification not completed")
    ).not.toBeInTheDocument()
  })

  it("names an unfinished ceremony as its own outcome, not as a failure", async () => {
    renderWith({ isSuccess: false, secondFactorIncomplete: true })

    expect(
      await screen.findByText("Two-factor verification not completed")
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        "Your password was entered but the verification code never followed."
      )
    ).toBeInTheDocument()
    expect(screen.queryByText("Failed sign-in")).not.toBeInTheDocument()
  })

  it("keeps showing the stored reason on a real failure", async () => {
    renderWith({ isSuccess: false, failureReason: "Invalid password" })

    expect(await screen.findByText("Failed sign-in")).toBeInTheDocument()
    expect(screen.getByText("Invalid password")).toBeInTheDocument()
  })

  it("counts rejected codes instead of listing one entry per guess", async () => {
    renderWith({
      isSuccess: false,
      secondFactorIncomplete: true,
      secondFactorAttempts: 3,
    })

    expect(await screen.findByText(/Rejected codes: 3/)).toBeInTheDocument()
  })

  it("omits the rejected-code line when there were none", async () => {
    renderWith({ isSuccess: true })

    expect(await screen.findByText("Signed in")).toBeInTheDocument()
    expect(screen.queryByText(/Rejected codes/)).not.toBeInTheDocument()
  })

  it("falls back to the old two-state reading when the field is absent", async () => {
    // An API that predates the field must not make every row look unfinished.
    renderWith({ isSuccess: true, secondFactorIncomplete: undefined })

    expect(await screen.findByText("Signed in")).toBeInTheDocument()
    expect(
      screen.queryByText("Two-factor verification not completed")
    ).not.toBeInTheDocument()
  })
})
