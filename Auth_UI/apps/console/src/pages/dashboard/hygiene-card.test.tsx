import { render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

/**
 * S24 PR B (FA12): the MFA adoption meter counts the factor a two-factor policy
 * accepts — the authenticator app — and the accounts whose only second factor is the
 * emailed code are counted apart, never inside the percentage.
 */
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

const { HygieneCard } = await import("./hygiene-card")

describe("HygieneCard", () => {
  it("counts the emailed code apart from the MFA adoption", () => {
    render(
      <HygieneCard
        mfaEnabled={4}
        emailOnlyTwoFactor={3}
        activeUsers={10}
        dormant30={0}
        dormant60={0}
        dormant90={0}
        neverLoggedIn={0}
        loading={false}
      />
    )

    expect(screen.getByText("40%")).toBeInTheDocument()
    expect(screen.getByTestId("email-only-two-factor")).toHaveTextContent(
      'dashboard.emailOnlyTwoFactorHint|{"count":3}'
    )
  })
})
