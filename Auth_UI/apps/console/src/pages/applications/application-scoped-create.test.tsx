import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The application page's Roles and Permissions tabs create inside that
 * application (owner, 2026-10-31): the dialog shows the application without
 * letting it change, posts it, and refreshes both the main list and the
 * application's own tab — the tab reads its rows under its own key, which the
 * main list's key alone never reached.
 */

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => mocks.get(...args),
    POST: (...args: unknown[]) => mocks.post(...args),
  },
}))

const { RoleFormDialog } = await import("../roles/role-form-dialog")
const { PermissionFormDialog } = await import(
  "../permissions/permission-form-dialog"
)

const APP = { id: "33333333-3333-3333-3333-333333333333", name: "Ledger" }

function ok<T>(data: T) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

const CASES = [
  {
    kind: "roles",
    Dialog: RoleFormDialog,
    path: "/api/v1/Roles",
    code: "ledger-clerk",
  },
  {
    kind: "permissions",
    Dialog: PermissionFormDialog,
    path: "/api/v1/Permissions",
    code: "ledger:read",
  },
] as const

beforeEach(async () => {
  await i18n.changeLanguage("en")
  // The shared applications list does NOT hold this application: the locked
  // picker must not depend on it.
  mocks.get.mockImplementation(() => ok({ applications: [], totalCount: 0 }))
  mocks.post.mockImplementation(() => ok({}))
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe.each(CASES)("creating $kind from an application's tab", ({ kind, Dialog, path, code }) => {
  function renderDialog() {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const invalidate = vi.spyOn(client, "invalidateQueries")
    render(
      <QueryClientProvider client={client}>
        <Dialog open onOpenChange={() => {}} lockedApplication={APP} />
      </QueryClientProvider>
    )
    return { invalidate }
  }

  it("shows the application without letting it change", async () => {
    renderDialog()
    const dialog = await screen.findByRole("dialog")

    const picker = within(dialog).getByRole("combobox")
    expect(picker).toBeDisabled()
    expect(picker).toHaveTextContent(APP.name)
  })

  it("posts the application and refreshes both lists", async () => {
    const user = userEvent.setup()
    const { invalidate } = renderDialog()
    const dialog = await screen.findByRole("dialog")

    await user.type(within(dialog).getByLabelText(i18n.t("common.code")), code)
    await user.type(within(dialog).getByLabelText(i18n.t("common.name")), "Clerk")
    await user.click(within(dialog).getByRole("button", { name: i18n.t("common.create") }))

    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    const [postedPath, init] = mocks.post.mock.calls[0] as [
      string,
      { body: { applicationId: string | null } },
    ]
    expect(postedPath).toBe(path)
    expect(init.body.applicationId).toBe(APP.id)

    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({
        queryKey: ["applications", APP.id, kind],
      })
    )
    expect(invalidate).toHaveBeenCalledWith({ queryKey: [kind] })
  })
})
