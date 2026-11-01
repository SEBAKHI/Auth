import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import type { Schemas } from "@authsystem/api/types"

/**
 * The Fonts tab (UI batch P4, item 7): per role, what is in use, a Latin and Arabic preview,
 * and upload / replace / remove. A picked file is checked in the browser first (WOFF2, at most
 * 2 MB, loadable) and previewed before anything is sent; Save sends it as multipart to the
 * role's endpoint; Remove asks first.
 *
 * Deliberate breaks, each turning a case red: drop the size check (the 2 MB + 1 case posts);
 * drop the signature check (the renamed PNG previews); send the request before the preview
 * (the file is posted on pick); remove without the confirmation.
 */

const put = vi.fn()
const del = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: vi.fn(),
    PUT: (...args: unknown[]) => put(...args),
    POST: vi.fn(),
    DELETE: (...args: unknown[]) => del(...args),
  },
}))

import { FontsTab } from "./fonts-tab"

class FakeFontFace {
  static fail = false
  family: string
  constructor(family: string) {
    this.family = family
  }
  load() {
    return FakeFontFace.fail ? Promise.reject(new Error("refused")) : Promise.resolve(this)
  }
}

const added: FakeFontFace[] = []

beforeEach(async () => {
  await i18n.changeLanguage("en")
  FakeFontFace.fail = false
  added.length = 0
  vi.stubGlobal("FontFace", FakeFontFace)
  Object.defineProperty(document, "fonts", {
    configurable: true,
    value: {
      add: (face: FakeFontFace) => added.push(face),
      delete: (face: FakeFontFace) => added.splice(added.indexOf(face), 1),
    },
  })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  put.mockReset()
  del.mockReset()
})

const settings = (fonts: Schemas["PlatformFontsDto"] = {}): Schemas["PlatformSettingsDto"] =>
  ({ platformName: "Northwind", fonts }) as Schemas["PlatformSettingsDto"]

function renderTab(fonts?: Schemas["PlatformFontsDto"]) {
  const onSaved = vi.fn()
  const onUnsavedChange = vi.fn()
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <FontsTab settings={settings(fonts)} onSaved={onSaved} onUnsavedChange={onUnsavedChange} />
    </QueryClientProvider>
  )
  return { onSaved, onUnsavedChange }
}

const woff2 = (name = "Brand.woff2", size = 64) => {
  const bytes = new Uint8Array(size)
  bytes.set([0x77, 0x4f, 0x46, 0x32]) // "wOF2"
  return new File([bytes], name, { type: "font/woff2" })
}

const card = (title: string) => screen.getByText(title).closest("[data-slot=card]") as HTMLElement

async function pick(role: "heading" | "body", file: File) {
  await userEvent.upload(screen.getByTestId(`font-file-${role}`), file, { applyAccept: false })
}

describe("FontsTab", () => {
  it("shows the shipped font for each role while none is uploaded, previewed in Latin and Arabic", () => {
    renderTab()

    const body = card("Body font")
    expect(within(body).getByText("IBM Plex Sans")).toBeInTheDocument()
    expect(within(body).getByText("Default")).toBeInTheDocument()
    const preview = body.querySelector("[data-slot=font-preview]")!
    expect(preview.querySelector('[lang="en"]')).not.toBeNull()
    expect(preview.querySelector('[lang="ar"]')).not.toBeNull()
    // A heading without its own font follows the body's, which is the shipped one here.
    expect(within(card("Heading font")).getByText("IBM Plex Sans")).toBeInTheDocument()
    expect(within(body).queryByRole("button", { name: "Remove font" })).toBeNull()
  })

  it("names the uploaded font, and a heading without its own follows it", () => {
    renderTab({ body: { url: "https://api.test/b.woff2", familyName: "Brand Sans" } })

    expect(within(card("Body font")).getByText("Brand Sans")).toBeInTheDocument()
    expect(within(card("Heading font")).getByText("Same as the body font")).toBeInTheDocument()
  })

  it("previews a picked file before sending it, then saves it as multipart to the role's endpoint", async () => {
    const saved = settings({ body: { url: "https://api.test/b.woff2", familyName: "Brand Sans" } })
    put.mockResolvedValue({ data: saved, response: new Response(null, { status: 200 }) })
    const { onSaved, onUnsavedChange } = renderTab()

    await pick("body", woff2())

    const body = card("Body font")
    await within(body).findByText("Not saved yet")
    expect(within(body).getByText("Brand.woff2")).toBeInTheDocument()
    expect(put).not.toHaveBeenCalled()
    expect(added).toHaveLength(1)
    // The page's one leave-prompt hears about the picked file.
    await waitFor(() => expect(onUnsavedChange).toHaveBeenLastCalledWith(expect.objectContaining({ isDirty: true })))
    const content = body.querySelector("[data-slot=font-preview] [data-slot=item-content]") as HTMLElement
    expect(content.style.fontFamily).toContain(added[0].family)

    await userEvent.click(within(body).getByRole("button", { name: "Save" }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(saved))
    const [path, init] = put.mock.calls[0]
    expect(path).toBe("/api/v1/admin/platform-settings/fonts/{role}")
    expect(init.params.path.role).toBe("body")
    const form = init.bodySerializer() as FormData
    expect((form.get("file") as File).name).toBe("Brand.woff2")
    expect(added).toHaveLength(0)
  })

  it.each([
    ["a file over 2 MB", woff2("Big.woff2", 2 * 1024 * 1024 + 1), "This file is larger than 2 MB."],
    [
      "a PNG renamed .woff2",
      new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], "logo.woff2"),
      "This file is not a WOFF2 font. Convert it to WOFF2 first.",
    ],
  ])("refuses %s in the browser, and sends nothing", async (_what, file, message) => {
    renderTab()

    await pick("heading", file)

    expect(await within(card("Heading font")).findByText(message)).toBeInTheDocument()
    expect(within(card("Heading font")).queryByText("Not saved yet")).toBeNull()
    expect(put).not.toHaveBeenCalled()
  })

  it("refuses a file the browser cannot load as a font", async () => {
    FakeFontFace.fail = true
    renderTab()

    await pick("body", woff2("Broken.woff2"))

    expect(
      await within(card("Body font")).findByText("This browser could not read the font. The file may be damaged.")
    ).toBeInTheDocument()
    expect(put).not.toHaveBeenCalled()
  })

  it("discards a picked file without sending it", async () => {
    renderTab()
    await pick("body", woff2())
    const body = card("Body font")
    await within(body).findByText("Not saved yet")

    await userEvent.click(within(body).getByRole("button", { name: "Discard" }))

    expect(within(body).queryByText("Not saved yet")).toBeNull()
    expect(added).toHaveLength(0)
    expect(put).not.toHaveBeenCalled()
  })

  it("removes a font only after asking, naming it", async () => {
    del.mockResolvedValue({ data: settings(), response: new Response(null, { status: 200 }) })
    const { onSaved } = renderTab({ heading: { url: "https://api.test/h.woff2", familyName: "Brand Display" } })

    await userEvent.click(within(card("Heading font")).getByRole("button", { name: "Remove font" }))
    expect(del).not.toHaveBeenCalled()
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Remove Brand Display?")).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole("button", { name: "Remove font" }))

    await waitFor(() => expect(onSaved).toHaveBeenCalled())
    expect(del.mock.calls[0][1].params.path.role).toBe("heading")
  })
})
