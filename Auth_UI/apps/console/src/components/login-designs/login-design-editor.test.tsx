import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { RouterProvider, createMemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * A design's texts in the editor: a row holds its texts by its own identity, never by its
 * key, so typing a key through another key's name, or leaving two rows on one key, loses no
 * text in any language; the document sent to save is built from the rows.
 *
 * Deliberate break: address the texts by key again (rename every language's entry from the
 * previous key to the next on each keystroke).
 */

const api = vi.hoisted(() => ({
  getDesign: vi.fn(),
  previewDesign: vi.fn(),
  saveDesign: vi.fn(),
}))

vi.mock("./login-design-api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./login-design-api")>()),
  ...api,
}))
vi.mock("./design-preview", () => ({ DesignPreview: () => null }))
vi.mock("@/pages/notifications/components/code-editor", () => ({
  CodeEditor: () => <div aria-label="code editor" />,
}))
vi.mock("@/pages/notifications/components/variable-palette", () => ({ VariablePalette: () => null }))

import { LoginDesignEditor } from "./login-design-editor"

const SCOPE = { kind: "application", applicationId: "app-1" } as const
const DESIGN = {
  id: "d1",
  applicationId: "app-1",
  name: "Autumn",
  content: "<h1>{{ strings.headline | raw }}</h1>",
  stringsJson: JSON.stringify({
    en: { headline: "Hello", text2: "World" },
    ar: { headline: "مرحبا", text2: "عالم" },
  }),
  showPlatformLogo: true,
  isActive: false,
  contentHash: "h1",
  revision: "2026-10-29T10:00:00Z",
  createdAt: "2026-10-29T10:00:00Z",
  warnings: [],
}

function renderEditor() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: (
          <LoginDesignEditor
            scope={SCOPE}
            designId="d1"
            canManage
            appearance={null}
            onClose={() => {}}
            onSaved={() => {}}
          />
        ),
      },
    ],
    { initialEntries: ["/"] }
  )
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}

const keyInputs = () => screen.getAllByRole("textbox", { name: "Key" })
const textOf = (key: string) => screen.getAllByRole("textbox", { name: `Text of ${key}` })
const saveButton = () => screen.getByRole("button", { name: "Save" })

async function savedStrings() {
  await waitFor(() => expect(api.saveDesign).toHaveBeenCalledTimes(1))
  return JSON.parse(api.saveDesign.mock.calls[0][2].stringsJson) as unknown
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  for (const mock of Object.values(api)) mock.mockReset()
  api.getDesign.mockResolvedValue(DESIGN)
  api.previewDesign.mockResolvedValue({ html: "", language: "en", dir: "ltr", warnings: [] })
  api.saveDesign.mockImplementation((_scope, _id, fields) =>
    Promise.resolve({ ...DESIGN, ...fields, revision: "2026-10-29T11:00:00Z" })
  )
})

afterEach(cleanup)

describe("LoginDesignEditor texts", () => {
  it("keeps every text when a key is typed through another key's name", async () => {
    const user = userEvent.setup()
    renderEditor()
    await waitFor(() => expect(keyInputs()).toHaveLength(2))

    // "headline2" passes through "headline", the first row's key, on its way.
    await user.clear(keyInputs()[1])
    await user.type(keyInputs()[1], "headline2")

    expect(keyInputs().map((input) => (input as HTMLInputElement).value)).toEqual(["headline", "headline2"])
    expect(textOf("headline")[0]).toHaveValue("Hello")
    expect(textOf("headline2")[0]).toHaveValue("World")
    await user.click(screen.getByRole("tab", { name: /^ar/i }))
    expect(textOf("headline")[0]).toHaveValue("مرحبا")
    expect(textOf("headline2")[0]).toHaveValue("عالم")

    await user.click(saveButton())
    expect(await savedStrings()).toEqual({
      en: { headline: "Hello", headline2: "World" },
      ar: { headline: "مرحبا", headline2: "عالم" },
    })
  })

  it("keeps both rows' texts while two rows share a key, and saves once they differ", async () => {
    const user = userEvent.setup()
    renderEditor()
    await waitFor(() => expect(keyInputs()).toHaveLength(2))

    await user.clear(keyInputs()[1])
    await user.type(keyInputs()[1], "headline")

    expect(textOf("headline").map((input) => (input as HTMLInputElement).value)).toEqual(["Hello", "World"])
    expect(keyInputs()[1]).toHaveAttribute("aria-invalid", "true")
    expect(saveButton()).toBeDisabled()

    await user.type(keyInputs()[1], "_more")
    expect(saveButton()).toBeEnabled()
    await user.click(saveButton())
    expect(await savedStrings()).toEqual({
      en: { headline: "Hello", headline_more: "World" },
      ar: { headline: "مرحبا", headline_more: "عالم" },
    })
  })

  it("is unchanged when it is opened, whatever order the stored texts come in", async () => {
    api.getDesign.mockResolvedValue({
      ...DESIGN,
      stringsJson: JSON.stringify({ en: { text2: "World" }, ar: { headline: "مرحبا", text2: "عالم" } }),
    })
    renderEditor()
    await waitFor(() => expect(keyInputs()).toHaveLength(2))

    expect(screen.queryByText("Unsaved changes")).toBeNull()
    expect(saveButton()).toBeDisabled()
  })
})
