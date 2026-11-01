import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type * as React from "react"
import { RouterProvider, createMemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { TooltipProvider } from "@authsystem/ui/tooltip"

/**
 * Deleting a record from its own page (UI batch P2, item 1): with unsaved edits on the page,
 * the confirmed delete goes back to the list without the "discard changes?" prompt, which
 * would otherwise offer to stay on a record that no longer exists. The global layout's page
 * keeps a disabled Delete instead of none.
 *
 * Deliberate break: navigate from the dialog's callback while the prompt still guards the page.
 */

const get = vi.fn()
const del = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: vi.fn(() => Promise.resolve({ data: {} })),
    PUT: vi.fn(),
    DELETE: (...args: unknown[]) => del(...args),
  },
}))
vi.mock("@authsystem/auth/auth-context", () => ({ useAuth: () => ({ hasPermission: () => true }) }))
vi.mock("./components/code-editor", () => ({ CodeEditor: () => <div aria-label="code editor" /> }))
vi.mock("./components/code-editor-utils", () => ({ insertAtCursor: () => false }))
vi.mock("./components/variable-palette", () => ({ VariablePalette: () => null }))
vi.mock("./components/preview-pane", () => ({ PreviewPane: () => null }))
vi.mock("./components/template-preview", () => ({ TemplatePreview: () => null }))
vi.mock("./components/manage-variables-dialog", () => ({ ManageVariablesDialog: () => null }))
vi.mock("./components/test-send-dialog", () => ({ TestSendDialog: () => null }))
vi.mock("./components/version-history-sheet", () => ({ VersionHistorySheet: () => null }))

import { NotificationLayoutDetailPage } from "./notification-layout-detail-page"
import { NotificationTemplateDetailPage } from "./notification-template-detail-page"

const LAYOUT_ID = "44444444-4444-4444-4444-444444444444"
const TEMPLATE_ID = "11111111-1111-1111-1111-111111111111"
const layout = {
  id: LAYOUT_ID,
  applicationId: "66666666-6666-6666-6666-666666666666",
  applicationName: "Customer portal",
  channel: "Email",
  name: "Portal email",
  draftContent: "<html>{{ content | raw }}</html>",
  draftStringsJson: "{}",
  isPublished: true,
  hasUnpublishedChanges: false,
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: "2026-08-21T07:00:00Z",
}
const template = {
  id: TEMPLATE_ID,
  notificationTypeId: "55555555-5555-5555-5555-555555555555",
  typeName: "Billing receipt",
  typeIsSystem: false,
  typeVariablesJson: "[]",
  applicationId: "66666666-6666-6666-6666-666666666666",
  applicationName: "Customer portal",
  channel: "Email",
  defaultLanguage: "en",
  draftVersionId: "22222222-2222-2222-2222-222222222222",
  publishedVersionId: null,
  draftVersion: { id: "22222222-2222-2222-2222-222222222222", versionNumber: 1, changeNote: null, translations: [] },
  publishedVersion: null,
  versions: [],
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: "2026-08-21T07:00:00Z",
}

function renderRoute(path: string, element: React.ReactNode, list: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  const router = createMemoryRouter(
    [
      { path: path.replace(/\/[0-9a-f-]+$/, "/:id"), element },
      { path: list, element: <h1>The list</h1> },
    ],
    { initialEntries: [path] }
  )
  render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <RouterProvider router={router} />
      </TooltipProvider>
    </QueryClientProvider>
  )
  return router
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  get.mockReset()
  del.mockReset()
  del.mockResolvedValue({ data: undefined, response: new Response(null, { status: 204 }) })
})

afterEach(cleanup)

describe("deleting from a record's own page", () => {
  it("leaves a layout with unsaved edits for the list, without asking to discard them", async () => {
    get.mockResolvedValue({ data: layout })
    const user = userEvent.setup()
    const router = renderRoute(`/notifications/layouts/${LAYOUT_ID}`, <NotificationLayoutDetailPage />, "/notifications/layouts")

    const name = await screen.findByRole("textbox", { name: "Layout name" })
    await user.type(name, " edited")
    await user.click(screen.getByRole("button", { name: "Delete" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Delete Portal email?")).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Delete" }))

    expect(await screen.findByRole("heading", { name: "The list" })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/notifications/layouts")
    expect(screen.queryByText("Discard changes?")).toBeNull()
    expect(del).toHaveBeenCalledWith("/api/v1/notification-layouts/{id}", { params: { path: { id: LAYOUT_ID } } })
  })

  it("keeps the global layout's Delete, disabled", async () => {
    get.mockResolvedValue({ data: { ...layout, applicationId: null, applicationName: null } })
    renderRoute(`/notifications/layouts/${LAYOUT_ID}`, <NotificationLayoutDetailPage />, "/notifications/layouts")

    expect(await screen.findByRole("button", { name: "Delete" })).toBeDisabled()
  })

  it("leaves a template with unsaved edits for the list, without asking to discard them", async () => {
    get.mockResolvedValue({ data: template })
    const user = userEvent.setup()
    const router = renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />,
      "/notifications/templates"
    )

    await user.type(await screen.findByRole("textbox", { name: "Subject" }), "Edited, not saved")
    // The page keeps Delete in its actions menu, with the other destructive actions.
    await user.click(screen.getByRole("button", { name: "Actions" }))
    await user.click(await screen.findByRole("menuitem", { name: "Delete" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Delete Billing receipt?")).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Delete" }))

    expect(await screen.findByRole("heading", { name: "The list" })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/notifications/templates")
    expect(screen.queryByText("Discard changes?")).toBeNull()
  })
})
