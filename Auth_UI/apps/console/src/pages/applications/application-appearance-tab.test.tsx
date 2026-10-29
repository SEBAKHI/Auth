import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { createMemoryRouter, RouterProvider } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { toThemeConfig, toThemeRequest } from "@authsystem/ui/theme/theme-config"

/**
 * An application's own appearance (an application › Appearance): a switch and
 * the platform's editor. It starts from the platform's appearance, takes no
 * edit while off, sends the whole appearance when on, and keeps the saved one
 * when switched off with nothing edited. Its preview is painted by the draft
 * alone: the console never takes an application's colours.
 */

const put = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: vi.fn(),
    PUT: (...args: unknown[]) => put(...args),
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useTheme: () => ({ resolvedTheme: "light" }),
  useResolvedTheme: () => "light",
}))

// The platform's appearance: Stone and Amber, large corners.
const PLATFORM = toThemeConfig({
  base: { preset: "stone" },
  theme: { preset: "amber" },
  chart: { preset: "cyan" },
  radius: "large",
  menuAccent: "subtle",
})
const setThemePreview = vi.fn()

vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({ isPending: false }),
  useThemePreview: () => ({ savedTheme: PLATFORM, setThemePreview }),
}))

import ApplicationAppearanceTab from "./application-appearance-tab"

const APP_ID = "11111111-1111-1111-1111-111111111111"
// The application's own: Mauve and Violet, small corners.
const OWN = {
  base: { preset: "mauve", light: null, dark: null },
  theme: { preset: "violet", light: null, dark: null },
  chart: { preset: "violet", light: null, dark: null },
  radius: "small",
  menuAccent: "bold",
  controlSize: { preset: "compact", px: null },
  tableRowHeight: { preset: "comfortable", px: null },
}

function application(overrides: Record<string, unknown> = {}) {
  return { id: APP_ID, code: "crm", name: "Example CRM", themeEnabled: false, theme: null, ...overrides }
}

function renderTab(app = application(), canUpdate = true) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  const router = createMemoryRouter(
    [{ path: "/", element: <ApplicationAppearanceTab application={app as never} canUpdate={canUpdate} /> }],
    { initialEntries: ["/"] }
  )
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
  return client
}

const theSwitch = () => screen.getByRole("switch", { name: "Customize this application's appearance" })
const baseColour = () => screen.getByRole("combobox", { name: "Base color" })
const saveButton = () => screen.getByRole("button", { name: "Save" })

beforeEach(async () => {
  await i18n.changeLanguage("en")
  put.mockImplementation((_path: string, init: { body: { enabled: boolean; theme: unknown } }) =>
    Promise.resolve({
      data: { enabled: init.body.enabled, theme: init.body.theme ?? OWN },
      response: new Response(null, { status: 200 }),
    })
  )
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
  document.head.innerHTML = ""
})

describe("application appearance tab", () => {
  it("starts off, from the platform's appearance, and takes no edit while off", () => {
    renderTab()

    expect(theSwitch()).not.toBeChecked()
    expect(baseColour()).toHaveTextContent("Stone")
    expect(baseColour().closest("fieldset")).toBeDisabled()
    expect(saveButton()).toBeDisabled()
  })

  it("switched on, saves the whole appearance it shows", async () => {
    renderTab()

    await userEvent.click(theSwitch())
    expect(baseColour()).toBeEnabled()
    await userEvent.click(saveButton())

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    const [path, init] = put.mock.calls[0]
    expect(path).toBe("/api/v1/Applications/{id}/appearance")
    expect(init.params).toEqual({ path: { id: APP_ID } })
    expect(init.body).toEqual({ enabled: true, theme: toThemeRequest(PLATFORM) })
  })

  it("shows a saved appearance, and switching it off with nothing edited sends no theme: the saved one stays", async () => {
    renderTab(application({ themeEnabled: true, theme: OWN }))

    expect(theSwitch()).toBeChecked()
    expect(baseColour()).toHaveTextContent("Mauve")
    await userEvent.click(theSwitch())
    await userEvent.click(saveButton())

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0][1].body).toEqual({ enabled: false, theme: null })
  })

  it("starts again from the platform's appearance on request", async () => {
    renderTab(application({ themeEnabled: true, theme: OWN }))

    await userEvent.click(screen.getByRole("button", { name: "Start from the platform's appearance" }))
    expect(baseColour()).toHaveTextContent("Stone")
    await userEvent.click(saveButton())

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0][1].body).toEqual({ enabled: true, theme: toThemeRequest(PLATFORM) })
  })

  it("paints its preview with the draft alone, and never the console", () => {
    const style = document.createElement("style")
    style.id = "platform-theme"
    style.textContent = "html:root{--radius:0.875rem;}"
    document.head.appendChild(style)

    renderTab(application({ themeEnabled: true, theme: OWN }))

    const scoped = document.querySelector('[data-appearance-scope="application-appearance"]')
    expect(scoped?.querySelectorAll('[data-mode="light"], [data-mode="dark"]')).toHaveLength(2)
    const sheets = [...document.head.querySelectorAll("style")].map((element) => element.textContent ?? "")
    const own = sheets.filter((text) => text.includes('[data-appearance-scope="application-appearance"]'))
    expect(own).toHaveLength(1)
    // Small corners: the application's, inside the scope only.
    expect(own[0]).toContain('[data-appearance-scope="application-appearance"] .light{')
    expect(own[0]).toContain("--radius:0.45rem;")
    expect(own[0]).not.toContain("html:root")
    expect(document.getElementById("platform-theme")?.textContent).toBe("html:root{--radius:0.875rem;}")
    expect(setThemePreview).not.toHaveBeenCalled()
  })

  it("is read-only without the right to change the application", () => {
    renderTab(application({ themeEnabled: true, theme: OWN }), false)

    expect(theSwitch()).toBeDisabled()
    expect(baseColour().closest("fieldset")).toBeDisabled()
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull()
  })
})
