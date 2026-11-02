import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import "@authsystem/i18n"

import { accountBarSnippet } from "./account-bar-snippet"
import { ApplicationAccountBarCard } from "./application-account-bar-card"

describe("ApplicationAccountBarCard", () => {
  it("offers the two lines with this deployment's accounts address, ready to copy", () => {
    expect(accountBarSnippet("https://accounts.contoso-sample.net")).toBe(
      '<script async src="https://accounts.contoso-sample.net/embed/v1/account-bar.js"></script>\n' +
        '<iam-account-bar signout-href="/sign-out"></iam-account-bar>'
    )
    render(<ApplicationAccountBarCard />)
    expect(screen.getByText(/embed\/v1\/account-bar\.js/)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Copy the code" })).toBeInTheDocument()
  })

  it("says plainly that it works only on a subdomain of the IAM site", () => {
    render(<ApplicationAccountBarCard />)
    expect(screen.getByText(/Works only for an application on a subdomain of the IAM site/)).toBeInTheDocument()
  })
})
