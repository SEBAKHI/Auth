import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import type { Schemas } from "@authsystem/api/types"
import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"

/**
 * The Languages tab (UI batch P5, item 11, owner decision D8): one switch per language, on while
 * it is shown in the language menus. English stays on, with the reason beside it. The switches
 * are a draft: Save sends the hidden codes to their own endpoint, Discard puts the stored ones
 * back, and the page's leave-prompt hears of unsaved switches.
 *
 * Deliberate breaks, each turning a case red: let English's switch move (the English case);
 * send the shown codes instead of the hidden ones (the body); drop the branding invalidation
 * (the menus would wait five minutes).
 */

const put = vi.fn()
const toastSuccess = vi.fn()
const toastError = vi.fn()

vi.mock("sonner", () => ({
  toast: {
    success: (...a: unknown[]) => toastSuccess(...a),
    error: (...a: unknown[]) => toastError(...a),
  },
}))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: vi.fn(),
    PUT: (...args: unknown[]) => put(...args),
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

import { LanguagesTab } from "./languages-tab"
import { PLATFORM_SETTINGS_QUERY_KEY } from "./platform-settings-tabs"

beforeEach(async () => {
  await i18n.changeLanguage("en")
})

afterEach(() => {
  cleanup()
  put.mockReset()
  toastSuccess.mockReset()
  toastError.mockReset()
})

const settings = (hiddenLanguages: string[] = []): Schemas["PlatformSettingsDto"] =>
  ({ platformName: "Northwind", hiddenLanguages }) as Schemas["PlatformSettingsDto"]

function renderTab(hiddenLanguages?: string[]) {
  const onUnsavedChange = vi.fn()
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  client.setQueryData(PLATFORM_SETTINGS_QUERY_KEY, settings(hiddenLanguages))
  const invalidate = vi.spyOn(client, "invalidateQueries")
  render(
    <QueryClientProvider client={client}>
      <LanguagesTab settings={settings(hiddenLanguages)} onUnsavedChange={onUnsavedChange} />
    </QueryClientProvider>
  )
  return { client, invalidate, onUnsavedChange }
}

describe("the Languages tab", () => {
  it("shows one switch per language, on unless hidden", () => {
    renderTab(["fr"])

    expect(screen.getAllByRole("switch")).toHaveLength(7)
    expect(screen.getByRole("switch", { name: "Français" })).not.toBeChecked()
    expect(screen.getByRole("switch", { name: "العربية" })).toBeChecked()
  })

  it("keeps English on and says why, so one language always stays", () => {
    renderTab()

    const english = screen.getByRole("switch", { name: "English" })
    expect(english).toBeChecked()
    expect(english).toBeDisabled()
    expect(english).toHaveAccessibleDescription("Always shown: every page falls back to English.")
  })

  it("saves the hidden codes in the menus' order, then refreshes the branding", async () => {
    put.mockResolvedValue({ data: settings(["ar", "zh"]) })
    const { client, invalidate, onUnsavedChange } = renderTab()
    const user = userEvent.setup()

    await user.click(screen.getByRole("switch", { name: "中文" }))
    await user.click(screen.getByRole("switch", { name: "العربية" }))
    expect(onUnsavedChange).toHaveBeenLastCalledWith({ isDirty: true, isSaving: false })
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put).toHaveBeenCalledWith("/api/v1/admin/platform-settings/hidden-languages", {
      body: { hiddenLanguages: ["ar", "zh"] },
    })
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: BRANDING_QUERY_KEY }))
    expect(client.getQueryData<Schemas["PlatformSettingsDto"]>(PLATFORM_SETTINGS_QUERY_KEY)?.hiddenLanguages)
      .toEqual(["ar", "zh"])
    expect(toastSuccess).toHaveBeenCalledWith("Languages saved.")
  })

  it("puts the stored switches back on Discard, with nothing sent", async () => {
    const { onUnsavedChange } = renderTab(["fa"])
    const user = userEvent.setup()

    await user.click(screen.getByRole("switch", { name: "فارسی" }))
    expect(screen.getByRole("switch", { name: "فارسی" })).toBeChecked()
    await user.click(screen.getByRole("button", { name: "Discard changes" }))

    expect(screen.getByRole("switch", { name: "فارسی" })).not.toBeChecked()
    expect(onUnsavedChange).toHaveBeenLastCalledWith({ isDirty: false, isSaving: false })
    expect(put).not.toHaveBeenCalled()
  })

  it("offers Save only once a switch has moved", async () => {
    renderTab()

    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled()
    await userEvent.setup().click(screen.getByRole("switch", { name: "Türkçe" }))
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled()
  })

  it("keeps the switches and says so when the server refuses", async () => {
    put.mockResolvedValue({
      error: { status: 400, code: "HiddenLanguages.DefaultLanguageRequired", detail: "English cannot be hidden." },
    })
    renderTab()
    const user = userEvent.setup()

    await user.click(screen.getByRole("switch", { name: "Türkçe" }))
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(toastError).toHaveBeenCalledTimes(1))
    expect(screen.getByRole("switch", { name: "Türkçe" })).not.toBeChecked()
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled()
  })
})
