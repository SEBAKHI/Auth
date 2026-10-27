import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { captureEntryHints, readFlowHints } from "@authsystem/i18n/flow-hints"
import { ThemeProvider, useTheme } from "@authsystem/ui/theme-provider"

const { get, put } = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    PUT: (...args: unknown[]) => put(...args),
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

vi.mock("@authsystem/api/use-profile-image", () => ({
  useProfileImage: () => ({ onChange: vi.fn(), onRemove: vi.fn(), pending: false }),
}))

import { ProfilePage } from "./profile-page"

/**
 * The profile form carries the profile's stored language and theme. Saving it
 * applies them only when the user changed that field: an application's
 * language or colour mode on this page (flow-hints.ts) outranks the stored ones
 * until the user picks, and saving a phone number is not picking.
 */

const PROFILE = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "jane@example.test",
  firstName: "Jane",
  lastName: "Doe",
  phoneNumber: "",
  preferredLanguage: "fr",
  timeZone: "UTC",
  theme: "light",
}

function ThemeProbe() {
  return <output aria-label="active theme">{useTheme().theme}</output>
}

function renderProfile() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <ThemeProvider defaultTheme="system" disableTransitionOnChange={false}>
        <MemoryRouter>
          <ProfilePage />
        </MemoryRouter>
        <ThemeProbe />
      </ThemeProvider>
    </QueryClientProvider>
  )
}

/** The picker inside the form item labelled `label` (the pickers carry no name of their own). */
function pickerOf(label: string) {
  const item = screen.getByText(label).parentElement as HTMLElement
  return within(item).getAllByRole("combobox")[0]
}

async function save(user: ReturnType<typeof userEvent.setup>) {
  const calls = put.mock.calls.length
  // By its role in the form, not its text: after a language change the label is no longer English.
  await user.click(document.querySelector<HTMLButtonElement>("form button[type=submit]")!)
  await waitFor(() => expect(put.mock.calls.length).toBe(calls + 1))
}

describe("saving the profile under an application's language and colour mode", () => {
  beforeEach(async () => {
    get.mockReset()
    put.mockReset()
    get.mockResolvedValue({ data: PROFILE })
    put.mockResolvedValue({ data: undefined, error: undefined })
    window.localStorage.clear()
    window.sessionStorage.clear()
    window.history.replaceState(null, "", "/profile?ui_locales=ar&ui_theme=dark")
    captureEntryHints({ pendingRequest: null, clientId: null, continuation: "never" })
    await i18n.changeLanguage("en")
  })

  afterEach(() => {
    window.sessionStorage.clear()
    window.history.replaceState(null, "", "/")
  })

  it("keeps them when the user saves another field", async () => {
    const user = userEvent.setup()
    renderProfile()
    await screen.findByLabelText("Phone number")

    await user.type(screen.getByLabelText("Phone number"), "+15550100")
    await save(user)

    await waitFor(() => expect(screen.getByLabelText("active theme")).toHaveTextContent("dark"))
    expect(i18n.language).toBe("en")
    expect(readFlowHints()).toEqual({ language: "ar", theme: "dark" })
    expect(window.localStorage.getItem("auth.language")).toBeNull()
    expect(window.localStorage.getItem("theme")).toBeNull()
  })

  it("applies, stores and ends the hint for the field the user changed", async () => {
    const user = userEvent.setup()
    renderProfile()
    await screen.findByLabelText("Phone number")

    await user.click(pickerOf("Theme"))
    await user.click(await screen.findByRole("option", { name: "Dark" }))
    await save(user)

    await waitFor(() => expect(window.localStorage.getItem("theme")).toBe("dark"))
    expect(readFlowHints()).toEqual({ language: "ar" })

    await user.click(pickerOf("Preferred language"))
    await user.click(await screen.findByRole("option", { name: "Türkçe" }))
    await save(user)

    await waitFor(() => expect(window.localStorage.getItem("auth.language")).toBe("tr"))
    expect(readFlowHints()).toEqual({})

    // The saved values are the new baseline: saving again changes nothing.
    await save(user)
    expect(window.localStorage.getItem("auth.language")).toBe("tr")
    await i18n.changeLanguage("en")
  })
})
