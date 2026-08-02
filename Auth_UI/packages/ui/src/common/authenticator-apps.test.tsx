import { describe, expect, it } from "vitest"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"

import "@authsystem/i18n"
import { AuthenticatorApps } from "./authenticator-apps"

describe("AuthenticatorApps", () => {
  it("lists the suggested apps on the enrolment screen", () => {
    render(<AuthenticatorApps />)

    for (const name of [
      "Google Authenticator",
      "Microsoft Authenticator",
      "Ente Auth",
      "Bitwarden Authenticator",
    ]) {
      expect(screen.getByText(name)).toBeInTheDocument()
    }
  })

  it("opens every link in a new tab without leaking the opener or referrer", () => {
    render(<AuthenticatorApps />)

    const links = screen.getAllByRole("link")
    expect(links).toHaveLength(4)
    for (const link of links) {
      expect(link).toHaveAttribute("target", "_blank")
      expect(link).toHaveAttribute("rel", "noopener noreferrer")
      expect(link.getAttribute("href")).toMatch(/^https:\/\//)
    }
  })

  it("keeps the list folded away on the code-entry screen until asked", async () => {
    const user = userEvent.setup()
    render(<AuthenticatorApps variant="disclosure" />)

    // A block of download links sitting open on a sign-in screen is
    // phishing-shaped; it has to be opt-in.
    expect(screen.queryByText("Google Authenticator")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button"))

    expect(screen.getByText("Google Authenticator")).toBeInTheDocument()
  })
})
