import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { createMemoryRouter, RouterProvider } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The branding card edits the platform's logos the way an application's page
 * does: ONE circle for the light and dark logos (a dialog holds one circle per
 * mode), and the favicon as its own circle beside it. Each save resends the
 * other fields as they are, so changing one slot never clears another. The API
 * is answered at the typed client.
 */

const get = vi.fn()
const put = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    PUT: (...args: unknown[]) => put(...args),
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useResolvedTheme: () => "light",
}))

// The appearance card and the sign-in page designs list are surfaces with tests
// of their own (appearance; components/login-designs); here they only mark their tab.
vi.mock("./appearance-card", () => ({ AppearanceCard: () => <p>appearance panel</p> }))
vi.mock("./login-designs-tab", () => ({ default: () => <p>designs panel</p> }))

// An upload the test lets finish when it chooses, and a frame editor that keeps
// the chosen file as it is (its own tests cover it).
const upload = vi.fn<(file: File) => Promise<{ key: string }>>()
vi.mock("@authsystem/api/upload", () => ({ uploadImage: (file: File) => upload(file) }))
vi.mock("@authsystem/ui/common/image-frame/image-frame-dialog", async () => {
  const React = await import("react")
  function KeepTheFile({ file, onConfirm }: { file: File; onConfirm: (file: File) => void }) {
    React.useEffect(() => onConfirm(file), [file, onConfirm])
    return null
  }
  return { default: KeepTheFile }
})

import { PlatformSettingsPage } from "./platform-settings-page"

// No two fields share a value, so a slot wired to the wrong field shows up.
const settings = {
  platformName: "Northwind Identity",
  logoUrl: "https://cdn.test/logo-light.png",
  logoUrlDark: "https://cdn.test/logo-dark.png",
  faviconUrl: "https://cdn.test/favicon.png",
}

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

function renderPage(initialEntry = "/admin/platform-settings") {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(
    [
      { path: "/admin/platform-settings", element: <PlatformSettingsPage /> },
      { path: "/admin/platform-settings/login-designs/:designId", element: <h1>A design</h1> },
    ],
    { initialEntries: [initialEntry] }
  )
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
  return router
}

/** The two circles, each named by the label under it. */
async function brandingCircles() {
  await screen.findByText("Branding")
  return [screen.getByRole("button", { name: "Logo" }), screen.getByRole("button", { name: "Favicon" })]
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  get.mockImplementation(() => ok(settings))
  // The server stores what it is sent, and answers with it.
  put.mockImplementation((_path: string, init: { body: Record<string, unknown> }) => ok({ ...settings, ...init.body }))
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe("platform settings branding card", () => {
  it("shows one logo circle and the favicon circle beside it", async () => {
    renderPage()

    expect(await brandingCircles()).toHaveLength(2)
    expect(screen.getByText("Logo")).toBeTruthy()
    expect(screen.getByText("Favicon")).toBeTruthy()
    // The per-mode labels belong to the dialog, not the branding card (the logo display
    // size card below it previews each mode under its own label).
    const card = within(screen.getByText("Branding").closest<HTMLElement>('[data-slot="card"]')!)
    expect(card.queryByText("Light mode")).toBeNull()
    expect(card.queryByText("Dark mode")).toBeNull()
  })

  it("edits the light and dark logos in one dialog, keeping the other fields", async () => {
    const user = userEvent.setup()
    renderPage()

    const [logo] = await brandingCircles()
    await user.click(logo!)
    // The page circle offers View and Change only; Remove lives per mode.
    expect(screen.queryByRole("menuitem", { name: "Remove" })).toBeNull()
    await user.click(await screen.findByRole("menuitem", { name: "Change" }))

    const dialog = await screen.findByRole("dialog", { name: "Platform logo" })
    expect(within(dialog).getByText("Light mode")).toBeTruthy()
    expect(within(dialog).getByText("Dark mode")).toBeTruthy()

    const darkSlot = dialog.querySelector<HTMLElement>('[data-mode="dark"]')!
    await user.click(within(darkSlot).getByRole("button", { name: "Dark mode" }))
    await user.click(await screen.findByRole("menuitem", { name: "Remove" }))

    await vi.waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]![1]).toEqual({
      body: {
        platformName: settings.platformName,
        logoUrl: settings.logoUrl,
        logoUrlDark: null,
        faviconUrl: settings.faviconUrl,
      },
    })
  })

  it("removes the favicon from its own circle, keeping both logos", async () => {
    const user = userEvent.setup()
    renderPage()

    const [, favicon] = await brandingCircles()
    await user.click(favicon!)
    await user.click(await screen.findByRole("menuitem", { name: "Remove" }))

    await vi.waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]![1]).toEqual({
      body: {
        platformName: settings.platformName,
        logoUrl: settings.logoUrl,
        logoUrlDark: settings.logoUrlDark,
        faviconUrl: null,
      },
    })
  })
})

describe("platform settings branding card — saves that overlap", () => {
  it("names each circle by the label under it, not as an avatar", async () => {
    renderPage()

    await screen.findByText("Branding")
    expect(screen.queryByRole("button", { name: "Avatar" })).toBeNull()
    expect(screen.getByRole("button", { name: "Logo" })).toBeTruthy()
    expect(screen.getByRole("button", { name: "Favicon" })).toBeTruthy()
  })

  it("a logo still uploading when the dark logo is removed does not bring the dark logo back", async () => {
    // Reported by the phase 2 review: the second save sent back the dark logo
    // its card had rendered before the first save removed it.
    const user = userEvent.setup()
    let finishUpload!: (value: { key: string }) => void
    upload.mockReturnValue(new Promise((resolve) => (finishUpload = resolve)))
    renderPage()

    const [logo] = await brandingCircles()
    await user.click(logo!)
    await user.click(await screen.findByRole("menuitem", { name: "Change" }))
    const dialog = await screen.findByRole("dialog", { name: "Platform logo" })
    const lightSlot = dialog.querySelector<HTMLElement>('[data-mode="light"]')!
    const darkSlot = dialog.querySelector<HTMLElement>('[data-mode="dark"]')!

    // A new light logo starts uploading…
    await user.upload(
      lightSlot.querySelector<HTMLInputElement>('input[type="file"]')!,
      new File(["png"], "light.png", { type: "image/png" })
    )
    await vi.waitFor(() => expect(upload).toHaveBeenCalledTimes(1))
    // …the dark logo is removed meanwhile, and that save lands…
    await user.click(within(darkSlot).getByRole("button", { name: "Dark mode" }))
    await user.click(await screen.findByRole("menuitem", { name: "Remove" }))
    await vi.waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]![1].body).toMatchObject({ logoUrl: settings.logoUrl, logoUrlDark: null })

    // …then the upload finishes: its save keeps the dark logo removed.
    finishUpload({ key: "uploads/new-light.png" })
    await vi.waitFor(() => expect(put).toHaveBeenCalledTimes(2))
    expect(put.mock.calls[1]![1]).toEqual({
      body: {
        platformName: settings.platformName,
        logoUrl: "uploads/new-light.png",
        logoUrlDark: null,
        faviconUrl: settings.faviconUrl,
      },
    })
  })
})

describe("platform settings branding card — two saves at once", () => {
  it("sends the second save only once the first has answered, and builds it on that answer", async () => {
    // Removing the dark logo, then the favicon before the first save answers:
    // both would read the stored copy at once, and the second would put the
    // dark logo back (or reach the server first).
    const user = userEvent.setup()
    let answerFirst!: () => void
    put.mockImplementationOnce(
      (_path: string, init: { body: Record<string, unknown> }) =>
        new Promise((resolve) => (answerFirst = () => resolve(ok({ ...settings, ...init.body }))))
    )
    renderPage()

    const [logo, favicon] = await brandingCircles()
    await user.click(logo!)
    await user.click(await screen.findByRole("menuitem", { name: "Change" }))
    const dialog = await screen.findByRole("dialog", { name: "Platform logo" })
    const darkSlot = dialog.querySelector<HTMLElement>('[data-mode="dark"]')!
    await user.click(within(darkSlot).getByRole("button", { name: "Dark mode" }))
    await user.click(await screen.findByRole("menuitem", { name: "Remove" }))
    await vi.waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    await user.keyboard("{Escape}")

    await user.click(favicon!)
    await user.click(await screen.findByRole("menuitem", { name: "Remove" }))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(put, "the favicon's save waits for the dark logo's answer").toHaveBeenCalledTimes(1)

    answerFirst()
    await vi.waitFor(() => expect(put).toHaveBeenCalledTimes(2))
    expect(put.mock.calls[1]![1].body).toEqual({
      platformName: settings.platformName,
      logoUrl: settings.logoUrl,
      logoUrlDark: null,
      faviconUrl: null,
    })
  })
})

/**
 * Item 8 of the UI batch: the page is tabs, not stacked cards, and the open tab lives in the
 * URL (the default writes none), so a tab can be linked, searched and reloaded. A tab once
 * opened stays mounted, so an unsaved edit survives a look at another tab.
 *
 * Deliberate breaks: hold the tab in state instead of the URL; drop the keep-mounted rule.
 */
describe("platform settings tabs", () => {
  it("opens on Identity with no tab parameter, and offers Identity, Appearance, Templates, Fonts and Languages in that order", async () => {
    const router = renderPage()

    expect(await screen.findByText("Branding")).toBeInTheDocument()
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Identity", "Appearance", "Templates", "Fonts", "Languages"])
    expect(screen.getByRole("tab", { name: "Identity" })).toHaveAttribute("aria-selected", "true")
    expect(router.state.location.search).toBe("")
    expect(screen.queryByText("designs panel")).toBeNull()
  })

  it("opens the tab a link names, and falls back to Identity for one that does not exist", async () => {
    renderPage("/admin/platform-settings?tab=templates")
    expect(await screen.findByText("designs panel")).toBeInTheDocument()
    expect(screen.queryByText("Branding")).toBeNull()
    cleanup()

    renderPage("/admin/platform-settings?tab=nonsense")
    expect(await screen.findByText("Branding")).toBeInTheDocument()
  })

  it("writes the open tab to the URL, and keeps an unsaved edit across a switch", async () => {
    const user = userEvent.setup()
    const router = renderPage()
    const name = await screen.findByRole("textbox", { name: "Platform name" })
    await user.clear(name)
    await user.type(name, "Draft name")

    await user.click(screen.getByRole("tab", { name: "Appearance" }))
    expect(router.state.location.search).toBe("?tab=appearance")
    expect(await screen.findByText("appearance panel")).toBeVisible()

    await user.click(screen.getByRole("tab", { name: "Identity" }))
    expect(router.state.location.search).toBe("")
    expect(screen.getByRole("textbox", { name: "Platform name" })).toHaveValue("Draft name")
    expect(put).not.toHaveBeenCalled()
  })

  it("asks before leaving the page with an unsaved name, and stops asking once it is saved", async () => {
    const user = userEvent.setup()
    const router = renderPage()
    const name = await screen.findByRole("textbox", { name: "Platform name" })
    await user.clear(name)
    await user.type(name, "Draft name")

    // Opening a design from the Templates tab leaves the page.
    await act(() => router.navigate("/admin/platform-settings/login-designs/d1"))
    const prompt = await screen.findByRole("alertdialog")
    expect(within(prompt).getByText("Discard changes?")).toBeInTheDocument()
    await user.click(within(prompt).getByRole("button", { name: "Cancel" }))
    expect(router.state.location.pathname).toBe("/admin/platform-settings")

    await user.click(within(name.closest("form")!).getByRole("button", { name: "Save" }))
    await waitFor(() => expect(put).toHaveBeenCalled())
    await act(() => router.navigate("/admin/platform-settings/login-designs/d1"))
    expect(await screen.findByRole("heading", { name: "A design" })).toBeInTheDocument()
  })
})
