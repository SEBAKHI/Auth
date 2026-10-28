import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
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

// The appearance card is its own surface with its own tests; these cases are
// about the branding card only.
vi.mock("./appearance-card", () => ({ AppearanceCard: () => null }))

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

function renderPage() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <PlatformSettingsPage />
    </QueryClientProvider>
  )
}

async function brandingCircles() {
  await screen.findByText("Branding")
  return screen.getAllByRole("button", { name: "Avatar" })
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  get.mockImplementation(() => ok(settings))
  put.mockImplementation(() => ok(settings))
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
    // The per-mode labels belong to the dialog, not the page.
    expect(screen.queryByText("Light mode")).toBeNull()
    expect(screen.queryByText("Dark mode")).toBeNull()
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
    await user.click(within(darkSlot).getByRole("button", { name: "Avatar" }))
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
