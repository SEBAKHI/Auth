import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { RouterProvider, createMemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

const get = vi.fn()
const post = vi.fn()
const put = vi.fn()
const hasPermission = vi.fn<(permission: string) => boolean>(() => true)

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
  CodeEditor: () => <div aria-label="code editor" />,
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
vi.mock("./components/test-send-dialog", () => ({ TestSendDialog: () => null }))
vi.mock("./components/variable-palette", () => ({ VariablePalette: () => null }))
vi.mock("./components/version-history-sheet", () => ({
  VersionHistorySheet: () => null,
}))
vi.mock("./components/preview-pane", () => ({ PreviewPane: () => null }))

import { NotificationTemplateDetailPage } from "./notification-template-detail-page"

const TEMPLATE_ID = "11111111-1111-1111-1111-111111111111"
const DRAFT_VERSION_ID = "22222222-2222-2222-2222-222222222222"

/**
 * A template whose draft carries ONE translation - the default language.
 *
 * This is the state every template starts in, and the state the seeded
 * development database never reproduces: its templates ship with all seven
 * languages already filled, which is why this defect was invisible there.
 */
const template = {
  id: TEMPLATE_ID,
  typeCode: "password-reset",
  notificationTypeId: "55555555-5555-5555-5555-555555555555",
  typeName: "Password reset",
  typeIsSystem: false,
  typeVariablesJson: "[]",
  applicationId: null,
  applicationName: null,
  channel: "Email",
  defaultLanguage: "en",
  draftVersionId: DRAFT_VERSION_ID,
  publishedVersionId: null,
  draftVersion: {
    id: DRAFT_VERSION_ID,
    versionNumber: 1,
    changeNote: null,
    translations: [
      {
        languageCode: "en",
        subject: "Reset your password",
        bodyHtml: "<p>Hello</p>",
        bodyText: "Hello",
      },
    ],
  },
  publishedVersion: null,
  versions: [],
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: "2026-08-21T07:00:00Z",
}

function renderPage() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  const router = createMemoryRouter(
    [
      {
        path: "/notifications/templates/:id",
        element: <NotificationTemplateDetailPage />,
      },
    ],
    { initialEntries: [`/notifications/templates/${TEMPLATE_ID}`] }
  )
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}

const languageTab = (code: string) =>
  screen.getByRole("tab", { name: new RegExp(`^${code}`, "i") })

describe("template editor language selection", () => {
  beforeEach(async () => {
    get.mockReset()
    post.mockReset()
    put.mockReset()
    hasPermission.mockReturnValue(true)
    await i18n.changeLanguage("en")
  })

  it("keeps a language that has no translation yet, so a new one can be started", async () => {
    get.mockResolvedValue({ data: template })
    const user = userEvent.setup()
    renderPage()

    await waitFor(() =>
      expect(languageTab("en")).toHaveAttribute("aria-selected", "true")
    )

    // Arabic has no translation on this template. Selecting it IS how a
    // translation is started, so the selection has to survive the click.
    await user.click(languageTab("ar"))

    await waitFor(() =>
      expect(languageTab("ar")).toHaveAttribute("aria-selected", "true")
    )
    expect(languageTab("en")).toHaveAttribute("aria-selected", "false")
  })

  it("opens the untranslated language on empty fields, not the default's text", async () => {
    get.mockResolvedValue({ data: template })
    const user = userEvent.setup()
    renderPage()

    await waitFor(() =>
      expect(languageTab("en")).toHaveAttribute("aria-selected", "true")
    )
    const subject = screen.getByLabelText(/subject/i)
    expect(subject).toHaveValue("Reset your password")

    await user.click(languageTab("ar"))

    // Showing the English subject here would silently invite the author to
    // publish the default language's text under an Arabic translation.
    await waitFor(() => expect(screen.getByLabelText(/subject/i)).toHaveValue(""))
  })

  it("returns to the default language when a different template is opened", async () => {
    get.mockResolvedValue({ data: template })
    const user = userEvent.setup()
    const { rerender } = renderPage()

    await waitFor(() =>
      expect(languageTab("en")).toHaveAttribute("aria-selected", "true")
    )
    await user.click(languageTab("ar"))
    await waitFor(() =>
      expect(languageTab("ar")).toHaveAttribute("aria-selected", "true")
    )

    // A different record arrives under the same route. The unsaved Arabic
    // choice belonged to the previous template and must not follow it here.
    get.mockResolvedValue({
      data: { ...template, id: "99999999-9999-9999-9999-999999999999" },
    })
    rerender(<div />)
    renderPage()

    await waitFor(() =>
      expect(languageTab("en")).toHaveAttribute("aria-selected", "true")
    )
  })
})
