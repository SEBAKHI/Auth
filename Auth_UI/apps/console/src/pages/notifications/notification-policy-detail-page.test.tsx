import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

const get = vi.fn()
const put = vi.fn()
const post = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    PUT: (...args: unknown[]) => put(...args),
    POST: (...args: unknown[]) => post(...args),
  },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ hasPermission: () => true }),
}))

vi.mock("@authsystem/ui/crumbs", () => ({ usePageBreadcrumb: vi.fn() }))
vi.mock("@authsystem/ui/hooks/use-unsaved-changes", () => ({
  useUnsavedChangesPrompt: () => null,
}))
vi.mock("./components/policy-field-editors", () => ({
  SectionListEditor: () => null,
  StringListEditor: () => null,
}))
vi.mock("./components/policy-language-gap-notice", () => ({
  PolicyLanguageGapNotice: () => null,
}))
vi.mock("./components/policy-preview-pane", () => ({
  PolicyPreviewPane: () => <div aria-label="policy preview" />,
}))
vi.mock("./components/policy-token-palette", () => ({
  PolicyTokenPalette: () => null,
}))
vi.mock("./components/policy-version-field", () => ({
  PolicyVersionField: ({
    id,
    value,
    disabled,
    onChange,
  }: {
    id: string
    value: string
    disabled?: boolean
    onChange: (value: string) => void
  }) => (
    <input
      id={id}
      value={value}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
}))

import { NotificationPolicyDetailPage } from "./notification-policy-detail-page"
import { parsePolicyDocument } from "./notification-policy-state"

const VERSION_ID = "11111111-1111-1111-1111-111111111111"
const versionRow = {
  id: VERSION_ID,
  version: "v1",
  effectiveDateUtc: "2026-08-21T00:00:00Z",
  changeNote: "Initial draft",
  isPublished: false,
  notifiedAtUtc: null,
  notifiedCount: 0,
  disclosureOutOfDate: false,
  languages: ["en"],
}

function renderPage() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/notifications/policy/${VERSION_ID}`]}>
        <Routes>
          <Route
            path="/notifications/policy/:id"
            element={<NotificationPolicyDetailPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

describe("parsePolicyDocument", () => {
  it("distinguishes unloaded, empty, valid, and malformed content", () => {
    expect(parsePolicyDocument(undefined, "en")).toMatchObject({
      doc: null,
      parseError: null,
      dirty: false,
    })

    const empty = parsePolicyDocument({ contentJson: null }, "ar")
    expect(empty.doc).toMatchObject({ title: "", versionLabel: "Version" })
    expect(empty.language).toBe("ar")

    expect(
      parsePolicyDocument({ contentJson: '{"title":"Published"}' }, "en").doc
    ).toEqual({ title: "Published" })

    expect(
      parsePolicyDocument({ contentJson: "{" }, "en").parseError
    ).toBeTruthy()
  })
})

describe("NotificationPolicyDetailPage", () => {
  beforeEach(() => {
    get.mockReset().mockImplementation((path: string) => {
      if (path.endsWith("/versions")) {
        return Promise.resolve({ data: [versionRow] })
      }
      if (path.endsWith("/versions/content")) {
        return Promise.resolve({ data: { contentJson: null } })
      }
      return Promise.resolve({ data: { disclosure: {} } })
    })
    put.mockReset().mockResolvedValue({ data: {} })
    post.mockReset().mockResolvedValue({ data: {} })
  })

  it("saves document edits under the old key before renaming the revision", async () => {
    const user = userEvent.setup()
    renderPage()

    const version = await screen.findByLabelText("Version")
    const title = await screen.findByLabelText("Title")
    await user.clear(version)
    await user.type(version, "v2")
    await user.type(title, "Privacy notice")
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(2))
    expect(put.mock.calls[0]?.[0]).toBe(
      "/api/v1/privacy-policy/versions/content"
    )
    expect(put.mock.calls[0]?.[1]).toMatchObject({
      body: { version: "v1", languageCode: "en" },
    })
    expect(put.mock.calls[1]?.[0]).toBe("/api/v1/privacy-policy/versions")
    expect(put.mock.calls[1]?.[1]).toMatchObject({
      body: { version: "v1", newVersion: "v2" },
    })
  })
})

/**
 * The document's own fields, and publishing it.
 *
 * The cases above cover saving and renaming. These cover the parts an author
 * actually types into - the metadata, the draft warning, a retention row - and
 * the publish call itself, which is the one action here that reaches real
 * readers.
 *
 * Fields are driven with `fireEvent.change` rather than `user.type`: this page
 * re-renders the whole document on each keystroke.
 */
describe("editing and publishing a policy revision", () => {
  beforeEach(() => {
    get.mockReset().mockImplementation((path: string) => {
      if (path.endsWith("/versions")) {
        return Promise.resolve({ data: [versionRow] })
      }
      if (path.endsWith("/versions/content")) {
        return Promise.resolve({ data: { contentJson: null } })
      }
      return Promise.resolve({ data: { disclosure: {} } })
    })
    put.mockReset().mockResolvedValue({ data: {} })
    post.mockReset().mockResolvedValue({ data: {} })
  })

  it("records a change note and a draft warning", async () => {
    renderPage()
    await screen.findByLabelText("policy preview")

    const note = screen.getByPlaceholderText("What changed in this revision?")
    fireEvent.change(note, { target: { value: "clarified retention" } })
    expect(note).toHaveValue("clarified retention")

    const warning = screen.getByPlaceholderText(/Draft — do not publish/i)
    fireEvent.change(warning, { target: { value: "not final" } })
    expect(warning).toHaveValue("not final")
  })

  it("adds a retention row and fills it in", async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByLabelText("policy preview")

    // The document ships with no retention rows, so one has to be added before
    // there is anything to type into.
    const add = screen
      .getAllByRole("button")
      .find((button) => /add|أضف|صف/i.test(button.textContent ?? ""))
    expect(add, "no add-row control found").toBeDefined()
    await user.click(add!)

    const boxes = screen.getAllByRole("textbox")
    const target = boxes.at(-1)!
    fireEvent.change(target, { target: { value: "12 months" } })
    expect(target).toHaveValue("12 months")
  }, 20_000)

  it("picks an effective date from the calendar", async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByLabelText("policy preview")

    // The control is a popover with a calendar, not an input: it can only be
    // reached through its trigger and then a day inside the grid.
    const trigger = document.getElementById("meta-effective")
    expect(trigger, "no effective-date trigger").not.toBeNull()
    await user.click(trigger!)

    const grid = await screen.findByRole("grid")
    const day = within(grid)
      .getAllByRole("button")
      .find((button) => !button.hasAttribute("disabled"))
    expect(day, "no selectable day").toBeDefined()
    await user.click(day!)

    await waitFor(() => expect(screen.queryByRole("grid")).toBeNull())
  }, 20_000)

  it("publishes the revision through the API", async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByLabelText("policy preview")

    const publish = screen
      .getAllByRole("button")
      .find((button) => /^(publish|نشر)$/i.test((button.textContent ?? "").trim()))
    expect(publish, "no publish control").toBeDefined()
    await user.click(publish!)

    const dialog = await screen.findByRole("alertdialog")
    const confirm = within(dialog)
      .getAllByRole("button")
      .find((button) => /publish|نشر/i.test(button.textContent ?? ""))
    await user.click(confirm!)

    await waitFor(() =>
      expect(
        post.mock.calls.some(([path]) =>
          String(path).includes("/privacy-policy/versions/publish")
        )
      ).toBe(true)
    )
  }, 20_000)

  it("discarding on a language switch really drops the edits", async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByLabelText("policy preview")

    const title = screen.getByLabelText("Title")
    fireEvent.change(title, { target: { value: "Typed then discarded" } })
    expect(title).toHaveValue("Typed then discarded")

    // Leaving a dirty language raises the discard prompt.
    const arabic = screen.getByRole("tab", { name: /^ar/i })
    await user.click(arabic)
    const dialog = await screen.findByRole("alertdialog")
    const discard = within(dialog)
      .getAllByRole("button")
      .find((button) => /^discard$/i.test((button.textContent ?? "").trim()))
    expect(discard, "no discard control").toBeDefined()
    await user.click(discard!)

    // Coming back must show the server's document, not the text the user just
    // threw away. Lowering the dirty flag alone kept that text alive here -
    // and with the page reporting no unsaved changes, the next Save wrote it.
    await user.click(screen.getByRole("tab", { name: /^en/i }))
    await waitFor(() => expect(screen.getByLabelText("Title")).toHaveValue(""))
  }, 20_000)
})
