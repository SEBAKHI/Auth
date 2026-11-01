import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type * as React from "react"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { TooltipProvider } from "@authsystem/ui/tooltip"

import { PERMISSIONS } from "@/lib/permissions"

/**
 * Item 1 of the UI batch, on the lists: every email template, application layout and draft
 * privacy-policy version can be deleted from its row's menu, after a confirmation that names
 * it. A row the server would refuse keeps the item, disabled, with the reason on hover: the
 * platform's template of a system type, the global layout, and any policy version that is
 * not a draft nobody was shown (`canDelete`). Without the manage permission there is no
 * Delete at all.
 *
 * Deliberate breaks: offer an enabled Delete on a refused row; drop the item from it;
 * delete without the confirmation; send the delete to another record.
 */

let held = new Set<string>()
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    hasPermission: (permission?: string) => !permission || held.has(permission),
  }),
}))

const get = vi.fn()
const del = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    DELETE: (...args: unknown[]) => del(...args),
    POST: vi.fn(),
    PUT: vi.fn(),
  },
}))

import { NotificationLayoutsPage } from "./notification-layouts-page"
import { NotificationPolicyPage } from "./notification-policy-page"
import { NotificationTemplatesPage } from "./notification-templates-page"

function ok<T>(data: T) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

const TEMPLATES = [
  { id: "t-sys", typeName: "Password reset", typeCode: "password-reset", typeIsSystem: true, applicationId: null, channel: "Email", isPublished: true, translationCount: 7 },
  { id: "t-app", typeName: "Billing receipt", typeCode: "billing-receipt", typeIsSystem: false, applicationId: "app-1", applicationName: "Billing", channel: "Email", isPublished: false, translationCount: 1 },
]
const LAYOUTS = [
  { id: "l-global", name: "Default email", applicationId: null, channel: "Email", isPublished: true },
  { id: "l-app", name: "Billing email", applicationId: "app-1", applicationName: "Billing", channel: "Email", isPublished: true },
]
const VERSIONS = [
  { id: "v-live", version: "2026.09", isPublished: true, canDelete: false, languages: ["en"], effectiveDateUtc: "2026-09-01T00:00:00Z" },
  { id: "v-draft", version: "2026.11", isPublished: false, canDelete: true, languages: [], effectiveDateUtc: "2026-11-01T00:00:00Z" },
]

function renderPage(page: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <MemoryRouter>{page}</MemoryRouter>
      </TooltipProvider>
    </QueryClientProvider>
  )
}

async function openMenu(rowText: string) {
  const row = (await screen.findByText(rowText)).closest("tr")!
  await userEvent.setup().click(within(row).getByRole("button", { name: "Actions" }))
  return screen.findByRole("menu")
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  held = new Set([
    PERMISSIONS.notificationTemplates.read,
    PERMISSIONS.notificationTemplates.manage,
    PERMISSIONS.notificationLayouts.manage,
    PERMISSIONS.privacyPolicy.read,
    PERMISSIONS.privacyPolicy.manage,
  ])
  get.mockImplementation((path: string) => {
    if (path === "/api/v1/notification-templates") return ok({ templates: TEMPLATES, totalPages: 1, totalCount: 2 })
    if (path === "/api/v1/notification-types") return ok([])
    if (path === "/api/v1/notification-layouts") return ok(LAYOUTS)
    if (path === "/api/v1/privacy-policy/versions") return ok(VERSIONS)
    return ok(null)
  })
  del.mockImplementation(() => Promise.resolve({ data: undefined, response: new Response(null, { status: 204 }) }))
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe("row-menu delete", () => {
  it("deletes an application's email template after a confirmation naming it", async () => {
    const user = userEvent.setup()
    renderPage(<NotificationTemplatesPage />)

    const menu = await openMenu("Billing receipt")
    await user.click(within(menu).getByRole("menuitem", { name: "Delete" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Delete Billing receipt?")).toBeInTheDocument()
    expect(del).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole("button", { name: "Delete" }))
    await waitFor(() =>
      expect(del).toHaveBeenCalledWith("/api/v1/notification-templates/{id}", { params: { path: { id: "t-app" } } })
    )
  })

  it("keeps Delete on the platform's system template, disabled, with the reason", async () => {
    renderPage(<NotificationTemplatesPage />)

    const menu = await openMenu("Password reset")
    const item = within(menu).getByRole("menuitem", { name: "Delete" })
    expect(item).toHaveAttribute("aria-disabled", "true")
    // Written in the menu, not on hover: keyboard and touch readers see it too.
    expect(within(menu).getByText(/system notification cannot be deleted/)).toBeVisible()
    expect(item).toHaveAccessibleDescription(/system notification cannot be deleted/)
  })

  it("deletes an application's layout, and keeps the global layout's Delete disabled with the reason", async () => {
    const user = userEvent.setup()
    renderPage(<NotificationLayoutsPage />)

    const globalMenu = await openMenu("Default email")
    const globalDelete = within(globalMenu).getByRole("menuitem", { name: "Delete" })
    expect(globalDelete).toHaveAttribute("aria-disabled", "true")
    expect(globalDelete).toHaveAccessibleDescription(/global layout/i)
    await user.keyboard("{Escape}")

    const menu = await openMenu("Billing email")
    await user.click(within(menu).getByRole("menuitem", { name: "Delete" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Delete Billing email?")).toBeInTheDocument()
    expect(within(dialog).getByText(/use the global layout from now on/)).toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: "Delete" }))
    await waitFor(() =>
      expect(del).toHaveBeenCalledWith("/api/v1/notification-layouts/{id}", { params: { path: { id: "l-app" } } })
    )
  })

  it("deletes a draft policy version, and keeps the published one's Delete disabled with the reason", async () => {
    const user = userEvent.setup()
    renderPage(<NotificationPolicyPage />)

    const liveMenu = await openMenu("2026.09")
    const refused = within(liveMenu).getByRole("menuitem", { name: "Delete" })
    expect(refused).toHaveAttribute("aria-disabled", "true")
    expect(within(liveMenu).getByText(/Only a draft that was never published or announced/)).toBeVisible()
    expect(refused).toHaveAccessibleDescription(/Only a draft that was never published or announced/)
    await user.keyboard("{Escape}")

    const menu = await openMenu("2026.11")
    await user.click(within(menu).getByRole("menuitem", { name: "Delete" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Delete version 2026.11?")).toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: "Delete" }))
    await waitFor(() =>
      expect(del).toHaveBeenCalledWith("/api/v1/privacy-policy/versions/{id}", { params: { path: { id: "v-draft" } } })
    )
  })

  it("offers no Delete without the manage permission", async () => {
    held = new Set([PERMISSIONS.notificationTemplates.read])
    renderPage(<NotificationTemplatesPage />)

    const menu = await openMenu("Billing receipt")
    expect(within(menu).getByRole("menuitem", { name: "View" })).toBeInTheDocument()
    expect(within(menu).queryByRole("menuitem", { name: "Delete" })).toBeNull()
  })
})
