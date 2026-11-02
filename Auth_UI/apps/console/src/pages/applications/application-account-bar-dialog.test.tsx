import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import type { Schemas } from "@authsystem/api/types"

import { ApplicationAccountBarDialog } from "./application-account-bar-dialog"

/**
 * The account bar's code for one application (owner, 2026-11-02). What has more than one
 * possible answer starts empty and must be chosen: the code appears only once nothing is
 * left open, so a guess (the first return address, perhaps a localhost one) never reaches
 * it unnoticed. A question with one answer is answered for the administrator.
 */

const BASE: Schemas["ApplicationDto"] = {
  id: "a1",
  code: "CMS",
  name: "Commissions",
  baseUrl: "https://cms.example.com/",
  initiateLoginUri: null,
  postLogoutRedirectUris: ["http://localhost:8765/signed-out", "https://cms.example.com/signed-out"],
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
})

function renderDialog(application: Schemas["ApplicationDto"] = BASE, onEditApplication = vi.fn()) {
  render(
    <ApplicationAccountBarDialog
      application={application}
      open
      onOpenChange={() => undefined}
      onEditApplication={onEditApplication}
    />
  )
  return onEditApplication
}

async function choose(user: ReturnType<typeof userEvent.setup>, label: string, option: string) {
  await user.click(screen.getByRole("combobox", { name: label }))
  await user.click(await screen.findByRole("option", { name: option }))
}

const HEADER = "Code for the application's header"

describe("ApplicationAccountBarDialog", () => {
  it("starts with nothing chosen and shows no code", () => {
    renderDialog()
    for (const name of ["Grid and avatar", "Grid only", "Avatar only", "The person's choice", "Light", "Dark"]) {
      expect(screen.getByRole("radio", { name })).toHaveAttribute("aria-checked", "false")
    }
    expect(screen.getByRole("textbox", { name: "The application's sign-out address" })).toHaveValue("")
    expect(screen.getByRole("combobox", { name: "Return address after sign-out" })).toHaveTextContent("Choose…")
    expect(screen.getByRole("combobox", { name: "Where Sign in goes" })).toHaveTextContent("Choose…")
    expect(screen.getByText("Choose every option above to get the code.")).toBeInTheDocument()
    expect(screen.queryByRole("heading", { name: HEADER })).toBeNull()
  })

  it("writes the code once every option is chosen, with the return address picked", async () => {
    const user = userEvent.setup()
    renderDialog()
    await user.click(screen.getByRole("radio", { name: "Avatar only" }))
    await user.click(screen.getByRole("radio", { name: "Dark" }))
    await user.type(screen.getByRole("textbox", { name: "The application's sign-out address" }), "/auth/out")
    await choose(user, "Where Sign in goes", "The application's base address: https://cms.example.com/")
    expect(screen.queryByRole("heading", { name: HEADER })).toBeNull()
    await choose(user, "Return address after sign-out", "https://cms.example.com/signed-out")

    expect(screen.getByRole("heading", { name: HEADER })).toBeInTheDocument()
    const codes = screen.getAllByRole("code").map((code) => code.textContent)
    expect(codes[0]).toContain(
      '<iam-account-bar signout-href="/auth/out" signin-href="https://cms.example.com/" hide="apps" theme="dark">'
    )
    expect(codes[1]).toMatch(/end-session\?client_id=CMS&post_logout_redirect_uri=https%3A%2F%2Fcms\.example\.com%2Fsigned-out$/)
  })

  it("answers a question with one possible answer by itself", async () => {
    const user = userEvent.setup()
    renderDialog({ ...BASE, baseUrl: null, postLogoutRedirectUris: ["https://cms.example.com/signed-out"] })
    expect(screen.queryByRole("combobox", { name: "Return address after sign-out" })).toBeNull()
    expect(screen.queryByRole("combobox", { name: "Where Sign in goes" })).toBeNull()
    await user.click(screen.getByRole("radio", { name: "Grid and avatar" }))
    await user.click(screen.getByRole("radio", { name: "The person's choice" }))
    await user.type(screen.getByRole("textbox", { name: "The application's sign-out address" }), "/out")
    const header = screen.getAllByRole("code")[0].textContent
    expect(header).toContain('<iam-account-bar signout-href="/out"></iam-account-bar>')
  })

  it("says why there is no code without a return address, and opens the application's editor", async () => {
    const user = userEvent.setup()
    const onEdit = renderDialog({ ...BASE, postLogoutRedirectUris: [] })
    expect(screen.getByText(/no return address after sign-out registered yet/)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Edit the application" }))
    expect(onEdit).toHaveBeenCalledOnce()
  })
})
