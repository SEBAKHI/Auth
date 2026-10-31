import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import type { Schemas } from "@authsystem/api/types"

/**
 * The organization form has no logo URL field (owner, 2026-10-31): the logo is
 * uploaded on the organization's page. Editing must not drop a logo by
 * omission — the API reads a null logo as "remove it" — so the form resends the
 * one it was opened with, and creation sends none.
 */

const mocks = vi.hoisted(() => ({ post: vi.fn(), put: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    POST: (...args: unknown[]) => mocks.post(...args),
    PUT: (...args: unknown[]) => mocks.put(...args),
  },
}))

const { OrganizationFormDialog } = await import("./organization-form-dialog")

const LOGO = "https://auth.example.test/uploads/images/acme.webp"

const ORGANIZATION = {
  id: "11111111-1111-1111-1111-111111111111",
  code: "acme",
  name: "Acme",
  contactEmail: "contact@acme.test",
  website: null,
  logoUrl: LOGO,
  description: null,
  isActive: true,
} as Schemas["OrganizationDetailDto"]

function ok() {
  return Promise.resolve({ data: {}, response: new Response(null, { status: 200 }) })
}

function renderForm(organization?: Schemas["OrganizationDetailDto"]) {
  const client = new QueryClient()
  return render(
    <QueryClientProvider client={client}>
      <OrganizationFormDialog
        open
        onOpenChange={() => {}}
        organization={organization}
      />
    </QueryClientProvider>
  )
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  mocks.post.mockImplementation(ok)
  mocks.put.mockImplementation(ok)
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe("the organization form", () => {
  it("offers no logo URL field", async () => {
    renderForm(ORGANIZATION)

    await screen.findByRole("dialog")
    expect(screen.queryByPlaceholderText("https://cdn.acme.com/logo.svg")).toBeNull()
    expect(screen.queryByText("Logo URL")).toBeNull()
  })

  it("keeps the organization's logo when it saves an edit", async () => {
    const user = userEvent.setup()
    renderForm(ORGANIZATION)

    const name = await screen.findByLabelText(i18n.t("common.name"))
    await user.clear(name)
    await user.type(name, "Acme Corp")
    await user.click(screen.getByRole("button", { name: i18n.t("common.save") }))

    await waitFor(() => expect(mocks.put).toHaveBeenCalledTimes(1))
    const [, init] = mocks.put.mock.calls[0] as [string, { body: Record<string, unknown> }]
    expect(init.body).toMatchObject({ name: "Acme Corp", logoUrl: LOGO })
  })

  // Timed like shadcn's forms, that is react-hook-form's defaults (owner,
  // 2026-10-31): judged on the first submit, then live on every change.
  it("judges nothing before the first submit, not even on leaving a field", async () => {
    const user = userEvent.setup()
    renderForm(ORGANIZATION)

    const name = await screen.findByLabelText(i18n.t("common.name"))
    await user.clear(name)
    await user.tab()
    const email = screen.getByLabelText(i18n.t("organizations.contactEmail"))
    await user.clear(email)
    await user.type(email, "contact@acme")
    await user.tab()

    expect(screen.queryByText(i18n.t("validation.required"))).toBeNull()
    expect(screen.queryByText(i18n.t("validation.email"))).toBeNull()
  })

  it("after a failed submit, removes a required error the moment a value is typed", async () => {
    const user = userEvent.setup()
    renderForm(ORGANIZATION)

    const name = await screen.findByLabelText(i18n.t("common.name"))
    await user.clear(name)
    await user.click(screen.getByRole("button", { name: i18n.t("common.save") }))
    expect(await screen.findByText(i18n.t("validation.required"))).toBeInTheDocument()
    expect(name).toHaveFocus()

    await user.type(name, "A")

    expect(name).toHaveFocus()
    await waitFor(() =>
      expect(screen.queryByText(i18n.t("validation.required"))).toBeNull()
    )
  })

  it("after a failed submit, keeps the contact email's error while malformed and drops it once valid", async () => {
    const user = userEvent.setup()
    renderForm(ORGANIZATION)

    const email = await screen.findByLabelText(i18n.t("organizations.contactEmail"))
    await user.clear(email)
    // Accepted by the browser's own type="email" check, refused by the app's rule.
    await user.type(email, "contact@acme")
    await user.click(screen.getByRole("button", { name: i18n.t("common.save") }))
    expect(await screen.findByText(i18n.t("validation.email"))).toBeInTheDocument()
    expect(mocks.put).not.toHaveBeenCalled()

    await user.type(email, "corp")
    expect(screen.getByText(i18n.t("validation.email"))).toBeInTheDocument()

    await user.type(email, ".test")
    expect(email).toHaveFocus()
    await waitFor(() =>
      expect(screen.queryByText(i18n.t("validation.email"))).toBeNull()
    )
  })

  it("sends no logo when it creates an organization", async () => {
    const user = userEvent.setup()
    renderForm()

    await user.type(await screen.findByLabelText(i18n.t("common.code")), "acme")
    await user.type(screen.getByLabelText(i18n.t("common.name")), "Acme")
    await user.type(
      screen.getByLabelText(i18n.t("organizations.contactEmail")),
      "contact@acme.test"
    )
    await user.click(screen.getByRole("button", { name: i18n.t("common.create") }))

    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    const [, init] = mocks.post.mock.calls[0] as [string, { body: Record<string, unknown> }]
    expect(init.body).not.toHaveProperty("logoUrl")
  })
})
