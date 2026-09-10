import { render, screen } from "@testing-library/react"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))
// The verification itself belongs to the shared page; this test is about the
// footer the accounts app adds to it.
vi.mock("@authsystem/auth/pages/two-factor-verify", () => ({
  TwoFactorVerifyPage: ({ footer }: { footer?: React.ReactNode }) => (
    <div>{footer}</div>
  ),
}))

import { clearPendingTwoFactorChallenge } from "@authsystem/auth/pending-challenge"

import { AccountsTwoFactorPage } from "./two-factor"

const AUTHORIZE =
  "https://api.example.com/api/v1/auth/authorize?client_id=app&state=xyz"

function renderWith(state: unknown) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: "/two-factor", state }]}>
      <Routes>
        <Route path="/two-factor" element={<AccountsTwoFactorPage />} />
      </Routes>
    </MemoryRouter>
  )
}

describe("AccountsTwoFactorPage", () => {
  beforeEach(() => clearPendingTwoFactorChallenge())

  it("the sign-up link carries returnTo, read from router state as the screen receives it", () => {
    // /two-factor is only ever entered by challenge(), which navigates with
    // state and no query string; a link built from location.search alone was
    // always bare here, and a relying party's visitor who chose to sign up
    // from this screen lost the pending request at the first hop.
    renderWith({ challengeToken: "c", returnTo: AUTHORIZE })

    expect(screen.getByRole("link", { name: /sign up/i })).toHaveAttribute(
      "href",
      `/register?returnTo=${encodeURIComponent(AUTHORIZE)}`
    )
  })

  it("links plainly to sign-up when no request is pending", () => {
    renderWith({ challengeToken: "c" })

    expect(screen.getByRole("link", { name: /sign up/i })).toHaveAttribute(
      "href",
      "/register"
    )
  })

  it("does not carry a returnTo that fails the allow-list", () => {
    renderWith({ challengeToken: "c", returnTo: "https://evil.example/authorize" })

    expect(screen.getByRole("link", { name: /sign up/i })).toHaveAttribute(
      "href",
      "/register"
    )
  })
})
