import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { createMemoryRouter, RouterProvider } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { SystemSettingsSection } from "../lib/sections"

/**
 * S24 PR B (ARR-0694): switching email sending OFF strands every account whose
 * second factor is the emailed code — no code can be sent, so each signs in with a
 * recovery code only. The save asks first, with that number, which the server
 * counts; switching it ON asks nothing.
 */
const mocks = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: { GET: mocks.get, PUT: mocks.put, POST: mocks.post },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options && "count" in options ? `${key}|${String(options.count)}` : key,
    i18n: { language: "en" },
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("@authsystem/auth/require-permission", () => ({
  RequirePermission: ({ children }: { children: unknown }) => children,
}))

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }))

const { SectionForm } = await import("./section-form")

function section(enabled: boolean): SystemSettingsSection {
  return {
    key: "Email",
    group: "Platform",
    editable: true,
    version: 1,
    rowVersion: null,
    fields: [
      {
        path: "Enabled",
        kind: "bool",
        effectiveValue: enabled,
        baselineValue: enabled,
        defaultValue: false,
        source: "File",
        readOnly: false,
        sensitive: false,
      },
    ],
  }
}

function renderForm(enabled: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  // A data router: the form's unsaved-changes prompt blocks navigation.
  const router = createMemoryRouter([
    { path: "/", element: <SectionForm section={section(enabled)} /> },
  ])
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}

async function flipAndSave() {
  const user = userEvent.setup()
  await user.click(screen.getByRole("switch"))
  const saves = screen.getAllByRole("button", { name: "common.save" })
  await user.click(saves[saves.length - 1])
  return user
}

describe("switching email sending off", () => {
  beforeEach(() => {
    mocks.get.mockReset()
    mocks.put.mockReset().mockResolvedValue({ data: section(false), error: undefined, response: { status: 200 } })
  })

  it("asks first, with the number of accounts whose second factor is the emailed code", async () => {
    mocks.get.mockResolvedValue({ data: { count: 3, passwordlessCount: 5 }, error: undefined })
    renderForm(true)

    await flipAndSave()

    const dialog = await screen.findByRole("alertdialog")
    expect(await within(dialog).findByTestId("email-code-accounts")).toHaveTextContent(
      "systemSettings.emailOffAccounts|3"
    )
    // And the accounts that sign in only with an emailed code (OI-215), which
    // email off leaves with no way in but a provider.
    expect(within(dialog).getByTestId("email-code-accounts")).toHaveTextContent(
      "systemSettings.emailOffPasswordlessAccounts|5"
    )
    expect(mocks.get).toHaveBeenCalledWith("/api/v1/admin/system-settings/email/two-factor-accounts")
    expect(mocks.put).not.toHaveBeenCalled()
  })

  it("still asks, and says so, when the count fails", async () => {
    mocks.get.mockResolvedValue({ data: undefined, error: { status: 500 } })
    renderForm(true)

    await flipAndSave()

    const dialog = await screen.findByRole("alertdialog")
    expect(await within(dialog).findByTestId("email-code-accounts")).toHaveTextContent(
      "systemSettings.emailOffAccountsUnknown"
    )
    expect(within(dialog).getByTestId("email-code-accounts")).toHaveTextContent(
      "systemSettings.emailOffPasswordlessAccountsUnknown"
    )
  })

  it("switching it on asks nothing and counts nothing", async () => {
    renderForm(false)

    await flipAndSave()

    // Positive control (review of #107, F7 (c)): the save went through, so the
    // absence of a dialog below is the form's answer, not a click that missed.
    await waitFor(() => expect(mocks.put).toHaveBeenCalledTimes(1))
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(mocks.get).not.toHaveBeenCalled()
  })
})
