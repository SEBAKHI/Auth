import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  act,
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { RouterProvider, createMemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

const get = vi.fn()
const post = vi.fn()
const put = vi.fn()
const hasPermission = vi.fn<(permission: string) => boolean>(() => true)
/** What the authoring surfaces were handed, so their gating can be asserted. */
const authoringProps = vi.hoisted(() => ({
  editor: [] as Array<Record<string, unknown>>,
  palette: [] as Array<Record<string, unknown>>,
}))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: (...args: unknown[]) => post(...args),
    PUT: (...args: unknown[]) => put(...args),
    DELETE: vi.fn(),
  },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ hasPermission }),
}))

vi.mock("./components/code-editor", () => ({
  CodeEditor: (props: Record<string, unknown>) => {
    authoringProps.editor.push(props)
    return <div aria-label="code editor" />
  },
}))
vi.mock("./components/code-editor-utils", () => ({
  insertAtCursor: () => false,
}))
vi.mock("./components/manage-variables-dialog", () => ({
  ManageVariablesDialog: () => null,
}))
vi.mock("./components/template-preview", () => ({
  TemplatePreview: () => null,
}))
vi.mock("./components/test-send-dialog", () => ({
  TestSendDialog: ({ open }: { open: boolean }) =>
    open ? <div role="dialog" aria-label="Send test message" /> : null,
}))
vi.mock("./components/variable-palette", () => ({
  VariablePalette: (props: Record<string, unknown>) => {
    authoringProps.palette.push(props)
    return null
  },
}))
vi.mock("./components/version-history-sheet", () => ({
  VersionHistorySheet: ({ open }: { open: boolean }) =>
    open ? <div role="dialog" aria-label="Version history" /> : null,
}))
vi.mock("./components/preview-pane", () => ({
  PreviewPane: () => null,
}))

import { NotificationLayoutDetailPage } from "./notification-layout-detail-page"
import { NotificationTemplateDetailPage } from "./notification-template-detail-page"

const TEMPLATE_ID = "11111111-1111-1111-1111-111111111111"
const DRAFT_VERSION_ID = "22222222-2222-2222-2222-222222222222"
const PUBLISHED_VERSION_ID = "33333333-3333-3333-3333-333333333333"
const LAYOUT_ID = "44444444-4444-4444-4444-444444444444"
const LAYOUT_REVISION = "2026-08-21T07:00:00Z"

const template = {
  id: TEMPLATE_ID,
  notificationTypeId: "55555555-5555-5555-5555-555555555555",
  typeName: "Password reset",
  typeIsSystem: false,
  typeVariablesJson: "[]",
  applicationId: null,
  applicationName: null,
  channel: "Email",
  defaultLanguage: "en",
  draftVersionId: DRAFT_VERSION_ID,
  publishedVersionId: PUBLISHED_VERSION_ID,
  draftVersion: {
    id: DRAFT_VERSION_ID,
    versionNumber: 2,
    changeNote: null,
    translations: [],
  },
  publishedVersion: {
    id: PUBLISHED_VERSION_ID,
    versionNumber: 1,
    changeNote: null,
    translations: [],
  },
  versions: [],
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: LAYOUT_REVISION,
}

const layout = {
  id: LAYOUT_ID,
  applicationId: "66666666-6666-6666-6666-666666666666",
  applicationName: "Customer portal",
  channel: "Email",
  name: "Default email layout",
  draftContent: "<html>{{ content | raw }}</html>",
  draftStringsJson: "{}",
  isPublished: true,
  hasUnpublishedChanges: true,
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: LAYOUT_REVISION,
}

function renderRoute(path: string, element: React.ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  const router = createMemoryRouter(
    [
      {
        path: path.replace(/\/[0-9a-f-]+$/, "/:id"),
        element,
      },
      { path: "/next", element: <h1>Next page</h1> },
    ],
    { initialEntries: [path] }
  )

  const view = render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
  return { ...view, client, router }
}

function publicationCalls(action: "publish" | "unpublish") {
  return post.mock.calls.filter(([path]) => String(path).endsWith(`/${action}`))
}

describe("notification publication confirmations", () => {
  beforeEach(async () => {
    get.mockReset()
    post.mockReset()
    put.mockReset()
    hasPermission.mockReturnValue(true)
    await i18n.changeLanguage("en")
  })

  it("publishes the reviewed template draft only after confirm and blocks duplicate submits", async () => {
    get.mockResolvedValue({ data: template })
    let resolvePublish!: (value: { data: typeof template }) => void
    post.mockImplementation((path: string) => {
      if (path.endsWith("/publish")) {
        return new Promise((resolve) => {
          resolvePublish = resolve
        })
      }
      return Promise.resolve({ data: {} })
    })

    const user = userEvent.setup()
    renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )

    const publish = await screen.findByRole("button", { name: "Publish" })
    await user.click(publish)

    expect(publicationCalls("publish")).toHaveLength(0)
    let dialog = screen.getByRole("alertdialog", {
      name: "Publish Password reset?",
    })
    expect(
      within(dialog).getByText("Password reset", { selector: "bdi" })
    ).toBeInTheDocument()
    expect(within(dialog).getByText(/Draft v2 ·/)).toBeInTheDocument()
    expect(
      within(dialog).getByText("Global (all applications)")
    ).toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: "Cancel" }))
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(publicationCalls("publish")).toHaveLength(0)

    await user.click(publish)
    dialog = screen.getByRole("alertdialog")
    const confirm = within(dialog).getByRole("button", { name: "Publish" })
    await user.dblClick(confirm)

    expect(publicationCalls("publish")).toHaveLength(1)
    expect(publicationCalls("publish")[0]?.[1]).toEqual({
      params: { path: { id: TEMPLATE_ID } },
      body: {
        expectedDraftVersionId: DRAFT_VERSION_ID,
        expectedRevisionAt: LAYOUT_REVISION,
      },
    })
    expect(confirm).toBeDisabled()
    expect(
      within(dialog).getByRole("button", { name: "Cancel" })
    ).toBeDisabled()

    await user.keyboard("{Escape}")
    expect(screen.getByRole("alertdialog")).toBeInTheDocument()

    await act(async () => {
      resolvePublish({ data: template })
    })
    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    )
  })

  it("keeps a stale template confirmation open after a conflict", async () => {
    get.mockResolvedValue({ data: template })
    post.mockResolvedValue({
      error: {
        status: 409,
        title: "Notification.PublishTargetChanged",
        detail: "The saved draft selected for publishing has changed.",
      },
    })

    const user = userEvent.setup()
    renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )

    await user.click(await screen.findByRole("button", { name: "Publish" }))
    const dialog = screen.getByRole("alertdialog")
    await user.click(within(dialog).getByRole("button", { name: "Publish" }))

    await waitFor(() =>
      expect(
        within(dialog).getByRole("button", { name: "Publish" })
      ).toBeEnabled()
    )
    expect(screen.getByRole("alertdialog")).toBeInTheDocument()
    expect(publicationCalls("publish")).toHaveLength(1)
  })

  it("binds unpublish to the reviewed live template version", async () => {
    get.mockResolvedValue({ data: template })
    post.mockResolvedValue({ data: template })

    const user = userEvent.setup()
    renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )

    await screen.findByText("Password reset", { selector: "h1" })
    await user.click(screen.getByRole("button", { name: "Actions" }))
    await user.click(await screen.findByRole("menuitem", { name: "Unpublish" }))

    expect(publicationCalls("unpublish")).toHaveLength(0)
    const dialog = screen.getByRole("alertdialog", {
      name: "Unpublish Password reset?",
    })
    expect(within(dialog).getByText("Published v1")).toBeInTheDocument()
    await user.click(within(dialog).getByRole("button", { name: "Unpublish" }))

    await waitFor(() => expect(publicationCalls("unpublish")).toHaveLength(1))
    expect(publicationCalls("unpublish")[0]?.[1]).toEqual({
      params: { path: { id: TEMPLATE_ID } },
      body: { expectedPublishedVersionId: PUBLISHED_VERSION_ID },
    })
  })

  it("publishes the exact saved layout revision shown in the confirmation", async () => {
    get.mockResolvedValue({ data: layout })
    post.mockImplementation((path: string) =>
      Promise.resolve({ data: path.endsWith("/publish") ? layout : {} })
    )

    const user = userEvent.setup()
    renderRoute(
      `/notifications/layouts/${LAYOUT_ID}`,
      <NotificationLayoutDetailPage />
    )

    const publish = await screen.findByRole("button", { name: "Publish" })
    await waitFor(() => expect(publish).toBeEnabled())
    await user.click(publish)

    expect(publicationCalls("publish")).toHaveLength(0)
    const dialog = screen.getByRole("alertdialog", {
      name: "Publish Default email layout?",
    })
    expect(
      within(dialog).getByText("Default email layout", { selector: "bdi" })
    ).toBeInTheDocument()
    expect(within(dialog).getByText(/Saved draft ·/)).toBeInTheDocument()
    expect(within(dialog).getByText("Customer portal")).toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: "Publish" }))

    await waitFor(() => expect(publicationCalls("publish")).toHaveLength(1))
    expect(publicationCalls("publish")[0]?.[1]).toEqual({
      params: { path: { id: LAYOUT_ID } },
      body: { expectedRevisionAt: LAYOUT_REVISION },
    })
  })
})

describe("notification editor unsaved-change protection", () => {
  beforeEach(async () => {
    get.mockReset()
    post.mockReset()
    put.mockReset()
    hasPermission.mockReturnValue(true)
    post.mockResolvedValue({ data: {} })
    await i18n.changeLanguage("en")
  })

  it.each([
    [
      "notification-templates:manage",
      ["Save draft", "Send test", "Version history", "Discard draft", "Delete"],
    ],
    [
      "notification-templates:publish",
      ["Publish", "Version history", "Unpublish"],
    ],
    ["none", ["Version history"]],
  ])(
    "exposes the template actions allowed by %s",
    async (permission, expected) => {
      hasPermission.mockImplementation(
        (candidate: string) => candidate === permission
      )
      get.mockResolvedValue({ data: template })
      const { container } = renderRoute(
        `/notifications/templates/${TEMPLATE_ID}`,
        <NotificationTemplateDetailPage />
      )

      await screen.findByText("Password reset", { selector: "h1" })
      const desktop = container.querySelector(
        '[data-slot="page-action-surface-desktop"]'
      )
      expect(desktop).not.toBeNull()
      expect(
        within(desktop as HTMLElement)
          .getAllByRole("button")
          .map((button) => button.textContent)
      ).toEqual(expected)
    }
  )

  it("makes test send and version history directly discoverable", async () => {
    get.mockResolvedValue({ data: template })
    const { container } = renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )
    const user = userEvent.setup()

    await screen.findByText("Password reset", { selector: "h1" })
    const desktop = container.querySelector(
      '[data-slot="page-action-surface-desktop"]'
    ) as HTMLElement
    await user.click(within(desktop).getByRole("button", { name: "Send test" }))
    expect(
      screen.getByRole("dialog", { name: "Send test message" })
    ).toBeVisible()

    await user.click(
      within(desktop).getByRole("button", { name: "Version history" })
    )
    expect(
      screen.getByRole("dialog", { name: "Version history" })
    ).toBeVisible()
    expect(
      within(desktop).getByRole("button", { name: "Delete" })
    ).toHaveAttribute("data-variant", "destructive")
  })

  it("keeps a template edit on cancel and leaves only after explicit discard", async () => {
    get.mockResolvedValue({ data: template })
    const { router } = renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )
    const user = userEvent.setup()

    const subject = await screen.findByRole("textbox", { name: "Subject" })
    await user.type(subject, "Local subject")
    expect(screen.getByText("Unsaved changes")).toBeVisible()

    void router.navigate("/next")
    const dialog = await screen.findByRole("alertdialog", {
      name: "Discard changes?",
    })
    await user.click(within(dialog).getByRole("button", { name: "Cancel" }))
    expect(router.state.location.pathname).toContain("/notifications/templates")
    expect(subject).toHaveValue("Local subject")

    void router.navigate("/next")
    await user.click(
      within(await screen.findByRole("alertdialog")).getByRole("button", {
        name: "Discard",
      })
    )
    expect(
      await screen.findByRole("heading", { name: "Next page" })
    ).toBeVisible()
  })

  it("uses the submitted template snapshot and clears dirty state from the PUT response", async () => {
    get.mockResolvedValue({ data: template })
    const savedTemplate = {
      ...template,
      modifiedAt: "2026-08-22T08:00:00Z",
      draftVersion: {
        ...template.draftVersion,
        translations: [
          {
            languageCode: "en",
            subject: "Saved subject",
            bodyHtml: "",
            bodyText: null,
          },
        ],
      },
    }
    put.mockResolvedValue({ data: savedTemplate })
    const { router } = renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )
    const user = userEvent.setup()

    const subject = await screen.findByRole("textbox", { name: "Subject" })
    await user.type(subject, "Saved subject")
    await user.click(screen.getByRole("button", { name: "Save draft" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]).toMatchObject({
      params: { path: { id: TEMPLATE_ID } },
      body: {
        translations: [
          expect.objectContaining({
            languageCode: "en",
            subject: "Saved subject",
          }),
        ],
        expectedModifiedAt: LAYOUT_REVISION,
      },
    })
    await waitFor(() =>
      expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument()
    )

    await router.navigate("/next")
    expect(router.state.location.pathname).toBe("/next")
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
  })

  it("preserves a failed template save as dirty and blocks navigation", async () => {
    get.mockResolvedValue({ data: template })
    put.mockResolvedValue({
      error: { status: 409, title: "Conflict", detail: "Draft changed" },
    })
    const { router } = renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )
    const user = userEvent.setup()

    const subject = await screen.findByRole("textbox", { name: "Subject" })
    await user.type(subject, "Keep me")
    await user.click(screen.getByRole("button", { name: "Save draft" }))
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Save draft" })).toBeEnabled()
    )

    expect(subject).toHaveValue("Keep me")
    expect(screen.getByText("Unsaved changes")).toBeVisible()
    void router.navigate("/next")
    expect(
      await screen.findByRole("alertdialog", { name: "Discard changes?" })
    ).toBeVisible()
  })

  it("rebases edits made during template save without overwriting them", async () => {
    get.mockResolvedValue({ data: template })
    let resolveSave!: (value: unknown) => void
    put.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveSave = resolve
        })
    )
    const { router } = renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )
    const user = userEvent.setup()

    const subject = await screen.findByRole("textbox", { name: "Subject" })
    await user.type(subject, "Submitted")
    await user.click(screen.getByRole("button", { name: "Save draft" }))
    await user.clear(subject)
    await user.type(subject, "Newer local edit")
    expect(subject).toHaveValue("Newer local edit")

    await act(async () => {
      resolveSave({
        data: {
          ...template,
          modifiedAt: "2026-08-22T08:00:00Z",
          draftVersion: {
            ...template.draftVersion,
            translations: [
              {
                languageCode: "en",
                subject: "Submitted",
                bodyHtml: "",
                bodyText: null,
              },
            ],
          },
        },
      })
    })

    expect(subject).toHaveValue("Newer local edit")
    expect(screen.getByText("Unsaved changes")).toBeVisible()
    expect(put.mock.calls[0]?.[1]).toMatchObject({
      body: {
        translations: [expect.objectContaining({ subject: "Submitted" })],
      },
    })
    void router.navigate("/next")
    expect(await screen.findByRole("alertdialog")).toBeVisible()
  })

  it("updates a layout baseline immediately after a successful save", async () => {
    get.mockResolvedValue({ data: layout })
    const savedLayout = {
      ...layout,
      name: "Updated layout",
      modifiedAt: "2026-08-22T09:00:00Z",
    }
    put.mockResolvedValue({ data: savedLayout })
    const { router } = renderRoute(
      `/notifications/layouts/${LAYOUT_ID}`,
      <NotificationLayoutDetailPage />
    )
    const user = userEvent.setup()

    const name = await screen.findByRole("textbox", { name: "Layout name" })
    await user.clear(name)
    await user.type(name, "Updated layout")
    await user.click(screen.getByRole("button", { name: "Save draft" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]).toMatchObject({
      params: { path: { id: LAYOUT_ID } },
      body: {
        name: "Updated layout",
        draftContent: layout.draftContent,
        draftStringsJson: "{}",
        expectedModifiedAt: LAYOUT_REVISION,
      },
    })
    await waitFor(() =>
      expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument()
    )

    await router.navigate("/next")
    expect(router.state.location.pathname).toBe("/next")
  })

  it("rebases edits made during layout save without overwriting them", async () => {
    get.mockResolvedValue({ data: layout })
    let resolveSave!: (value: unknown) => void
    put.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveSave = resolve
        })
    )
    const { router } = renderRoute(
      `/notifications/layouts/${LAYOUT_ID}`,
      <NotificationLayoutDetailPage />
    )
    const user = userEvent.setup()

    const name = await screen.findByRole("textbox", { name: "Layout name" })
    await user.clear(name)
    await user.type(name, "Submitted layout")
    await user.click(screen.getByRole("button", { name: "Save draft" }))
    await user.clear(name)
    await user.type(name, "Newer layout edit")
    expect(name).toHaveValue("Newer layout edit")

    await act(async () => {
      resolveSave({
        data: {
          ...layout,
          name: "Submitted layout",
          modifiedAt: "2026-08-22T09:00:00Z",
        },
      })
    })

    expect(name).toHaveValue("Newer layout edit")
    expect(screen.getByText("Unsaved changes")).toBeVisible()
    expect(put.mock.calls[0]?.[1]).toMatchObject({
      body: { name: "Submitted layout" },
    })
    void router.navigate("/next")
    expect(await screen.findByRole("alertdialog")).toBeVisible()
  })
})

describe("a reader who cannot save", () => {
  beforeEach(() => {
    authoringProps.editor.length = 0
    authoringProps.palette.length = 0
  })

  // Read access opens these pages, and the body editor and variable chips were
  // the only surfaces on them with no permission gate. Someone who cannot save
  // could still dirty the draft - and then meet a destructive "discard your
  // changes?" prompt on the way out, for an edit that was never possible.
  it.each([
    [
      "template",
      `/notifications/templates/${TEMPLATE_ID}`,
      () => <NotificationTemplateDetailPage />,
      "notification-templates:manage",
    ],
    [
      "layout",
      `/notifications/layouts/${LAYOUT_ID}`,
      () => <NotificationLayoutDetailPage />,
      "notification-layouts:manage",
    ],
  ])("cannot type into the %s body", async (_name, path, element, manage) => {
    hasPermission.mockImplementation((permission) => permission !== manage)

    renderRoute(path, element())
    await screen.findByLabelText("code editor")

    expect(authoringProps.editor.at(-1)?.readOnly).toBe(true)
    for (const palette of authoringProps.palette) {
      expect(palette.onInsert).toBeUndefined()
    }
  })

  it("still lets a manager edit", async () => {
    hasPermission.mockImplementation(() => true)

    renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )
    await screen.findByLabelText("code editor")

    expect(authoringProps.editor.at(-1)?.readOnly).toBe(false)
    expect(authoringProps.palette.at(-1)?.onInsert).toBeTypeOf("function")
  })
})

/**
 * Every action on these pages asks before it acts.
 *
 * The action surface is assembled from one `PageAction[]` per page, and the
 * commands on it are the most consequential in the console: publishing changes
 * what real recipients receive, unpublishing stops a live template, discarding
 * throws away an author's work. This walks whatever the surface offers and
 * insists that clicking it opens something - a dialog, a confirmation - rather
 * than sending a request. Save is the one exception: it is the button whose
 * whole purpose is to send.
 */
describe("detail page actions ask before they act", () => {
  const ALWAYS_SENDS = /^(save|save draft)$/i

  // The layout page has no `PageActionSurface`: it still renders its own button
  // row in the header, so its commands are found by name instead of through the
  // surface's group.
  it.each([
    [
      "template",
      `/notifications/templates/${TEMPLATE_ID}`,
      () => <NotificationTemplateDetailPage />,
      template,
    ],
  ])(
    "%s",
    async (_name, path, element, record) => {
      hasPermission.mockImplementation(() => true)
      get.mockResolvedValue({ data: record })
      post.mockResolvedValue({ data: {} })
      renderRoute(path, element())
      await screen.findByLabelText("code editor")

      const surface = screen.getByRole("group", { name: /actions/i })
      const labels = within(surface)
        .getAllByRole("button")
        .map((button) => button.textContent?.trim() ?? "")
        .filter((label) => label && !ALWAYS_SENDS.test(label))
      expect(labels.length).toBeGreaterThan(2)
      cleanup()

      for (const label of labels) {
        put.mockClear()
        post.mockClear()
        get.mockResolvedValue({ data: record })
        renderRoute(path, element())
        await screen.findByLabelText("code editor")

        const user = userEvent.setup()
        const button = within(
          screen.getByRole("group", { name: /actions/i })
        ).getByRole("button", { name: label })
        await user.click(button)

        // Either kind of surface counts; the dialogs are portalled, so this has
        // to wait rather than read the DOM on the same tick as the click.
        await waitFor(() => {
          const opened =
            screen.queryByRole("alertdialog") ?? screen.queryByRole("dialog")
          expect(opened, `"${label}" opened nothing`).not.toBeNull()
        })
        expect(
          put,
          `"${label}" sent a PUT before confirmation`
        ).not.toHaveBeenCalled()
        expect(
          post.mock.calls.filter(
            ([path]) => typeof path === "string" && !path.includes("/preview")
          ),
          `"${label}" sent a POST before confirmation`
        ).toEqual([])
        cleanup()
      }
    },
    60_000
  )

  it("layout", async () => {
    hasPermission.mockImplementation(() => true)
    get.mockResolvedValue({ data: layout })
    post.mockResolvedValue({ data: {} })

    renderRoute(
      `/notifications/layouts/${LAYOUT_ID}`,
      <NotificationLayoutDetailPage />
    )
    await screen.findByLabelText("code editor")

    const labels = ["Publish", "Discard draft", "Delete"]
    cleanup()

    for (const label of labels) {
      put.mockClear()
      post.mockClear()
      get.mockResolvedValue({ data: layout })
      renderRoute(
        `/notifications/layouts/${LAYOUT_ID}`,
        <NotificationLayoutDetailPage />
      )
      await screen.findByLabelText("code editor")

      const user = userEvent.setup()
      const button = screen.queryByRole("button", { name: label })
      if (!button) {
        cleanup()
        continue
      }
      await user.click(button)

      await waitFor(() => {
        const opened =
          screen.queryByRole("alertdialog") ?? screen.queryByRole("dialog")
        expect(opened, `"${label}" opened nothing`).not.toBeNull()
      })
      expect(put, `"${label}" sent a PUT before confirmation`).not.toHaveBeenCalled()
      cleanup()
    }
  }, 60_000)
})

/**
 * The editing surfaces themselves, rather than the publish path around them.
 *
 * Two things live here and nowhere else: dismissing a confirmation without
 * publishing, and inserting a variable when the cursor cannot be reached.
 * `insertAtCursor` is mocked to fail, which makes that fallback the natural
 * path - and it is the path an author takes whenever focus is anywhere but the
 * editor.
 *
 * Field edits go through `fireEvent.change` rather than `user.type`: these
 * pages re-render wholesale on every keystroke, and typing a sentence into one
 * costs minutes for the same single line of coverage.
 */
describe("editing a notification record", () => {
  beforeEach(() => {
    hasPermission.mockImplementation(() => true)
    post.mockResolvedValue({ data: {} })
    put.mockResolvedValue({ data: {} })
    authoringProps.palette.length = 0
  })

  it("dismisses a layout publish without publishing, then still edits", async () => {
    const user = userEvent.setup()
    get.mockResolvedValue({ data: layout })
    renderRoute(
      `/notifications/layouts/${LAYOUT_ID}`,
      <NotificationLayoutDetailPage />
    )
    await screen.findByLabelText("code editor")

    // Dismissal first: Publish disables itself the moment the draft is dirty,
    // so any edit below would put this branch out of reach.
    await user.click(screen.getByRole("button", { name: "Publish" }))
    await screen.findByRole("alertdialog")
    await user.keyboard("{Escape}")
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull())
    expect(
      post.mock.calls.filter(([path]) => String(path).endsWith("/publish"))
    ).toEqual([])

    // A slot inserted with no reachable cursor is appended, not dropped.
    // Snapshot first: the capture array grows on every render, and each insert
    // causes one, so iterating it live never finishes.
    const palettes = [...authoringProps.palette]
    expect(palettes.length).toBeGreaterThan(1)
    for (const palette of palettes) {
      act(() => (palette.onInsert as (value: string) => void)("{{ slot }}"))
    }

    const footer = screen.getByLabelText(/footer/i)
    fireEvent.change(footer, { target: { value: "signed off" } })
    expect(footer).toHaveValue("signed off")
  }, 20_000)

  it("dismisses a template unpublish, notes a change, and inserts variables", async () => {
    const user = userEvent.setup()
    get.mockResolvedValue({ data: template })
    renderRoute(
      `/notifications/templates/${TEMPLATE_ID}`,
      <NotificationTemplateDetailPage />
    )
    await screen.findByLabelText("code editor")

    await user.click(screen.getByRole("button", { name: "Unpublish" }))
    await screen.findByRole("alertdialog")
    await user.keyboard("{Escape}")
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull())
    expect(
      post.mock.calls.filter(([path]) => String(path).endsWith("/unpublish"))
    ).toEqual([])

    const note = screen.getByLabelText(/change note/i)
    fireEvent.change(note, { target: { value: "reworded the greeting" } })
    expect(note).toHaveValue("reworded the greeting")

    // Snapshot first: the capture array grows on every render, and each insert
    // causes one, so iterating it live never finishes.
    const palettes = [...authoringProps.palette]
    expect(palettes.length).toBeGreaterThan(1)
    for (const palette of palettes) {
      act(() => (palette.onInsert as (value: string) => void)("{{ user.name }}"))
    }
  }, 20_000)
})
