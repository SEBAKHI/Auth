import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { setHiddenLanguages } from "@authsystem/i18n/hidden-languages"

import { BrandingProvider } from "../branding"
import { LanguageSelect } from "./language-select"
import { LanguageToggle } from "./language-toggle"

/**
 * The language menus (UI batch P5, item 11, owner decision D8): the header toggle of both apps
 * and of every sign-in page, and the select of the profile and of the console's user form,
 * list only the languages an administrator has not hidden. The list comes with the branding.
 *
 * Deliberate breaks, each turning a case here red: map `SUPPORTED_LANGUAGES` again in either
 * menu; drop the `setHiddenLanguages` hand-off from `BrandingProvider`; render the toggle with
 * English alone.
 */

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))
vi.mock("@authsystem/ui/theme-provider", () => ({
  useTheme: () => ({ resolvedTheme: "light" }),
}))
vi.mock("@authsystem/i18n/direction", () => ({
  useLanguage: () => ({ language: "en", dir: "ltr", setLanguage: vi.fn() }),
}))
vi.mock("@authsystem/ui/common/use-preference-sync", () => ({
  usePreferenceSync: () => vi.fn(),
}))

const get = vi.fn()
vi.mock("@authsystem/api/client", () => ({ api: { GET: (...a: unknown[]) => get(...a) } }))
vi.mock("@authsystem/api/helpers", () => ({
  unwrap: (promise: Promise<{ data: unknown }>) => promise.then((r) => r.data),
}))
vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.test" }))

afterEach(() => {
  act(() => setHiddenLanguages([]))
  get.mockReset()
})

async function openToggle() {
  await userEvent.setup().click(screen.getByRole("button", { name: "common.language" }))
  return screen.findByRole("menu")
}

describe("the language toggle", () => {
  it("lists every language while nothing is hidden", async () => {
    render(<LanguageToggle />)

    const menu = await openToggle()

    expect(within(menu).getAllByRole("menuitem").map((item) => item.textContent)).toEqual([
      "English",
      "العربية",
      "Türkçe",
      "Français",
      "中文",
      "اردو",
      "فارسی",
    ])
  })

  it("leaves hidden languages out, and English in", async () => {
    act(() => setHiddenLanguages(["fr", "fa", "en"]))
    render(<LanguageToggle />)

    const menu = await openToggle()

    const items = within(menu).getAllByRole("menuitem").map((item) => item.textContent)
    expect(items).toEqual(["English", "العربية", "Türkçe", "中文", "اردو"])
  })

  it("is not offered at all once English is the only language left", () => {
    act(() => setHiddenLanguages(["ar", "tr", "fr", "zh", "ur", "fa"]))
    render(<LanguageToggle />)

    expect(screen.queryByRole("button", { name: "common.language" })).toBeNull()
  })
})

describe("the language select", () => {
  it("leaves hidden languages out of its options", async () => {
    act(() => setHiddenLanguages(["zh"]))
    render(<LanguageSelect value="en" onChange={vi.fn()} />)

    await userEvent.setup().click(screen.getByRole("combobox"))

    const options = (await screen.findAllByRole("option")).map((option) => option.textContent)
    expect(options).not.toContain("中文")
    expect(options).toHaveLength(6)
  })

  it("shows a saved language that is hidden now as unset, so another one can be picked", () => {
    act(() => setHiddenLanguages(["tr"]))
    render(<LanguageSelect value="tr" onChange={vi.fn()} />)

    expect(screen.getByRole("combobox")).toHaveTextContent("common.selectLanguage")
  })
})

describe("the branding hands the list to the menus", () => {
  it("hides what the branding hides, and shows it again when the branding stops hiding it", async () => {
    get.mockResolvedValue({ data: { platformName: "Acme", hiddenLanguages: ["ar", "ur"] } })
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const view = render(
      <QueryClientProvider client={client}>
        <BrandingProvider>
          <LanguageToggle />
        </BrandingProvider>
      </QueryClientProvider>
    )

    let menu = await openToggle()
    await waitFor(() => expect(within(menu).queryByText("العربية")).toBeNull())
    expect(within(menu).queryByText("اردو")).toBeNull()
    await userEvent.setup().keyboard("{Escape}")

    // An older API (or a copy cached before the setting) says nothing: none hidden.
    get.mockResolvedValue({ data: { platformName: "Acme" } })
    await act(() => client.invalidateQueries())
    menu = await openToggle()
    await waitFor(() => expect(within(menu).getByText("العربية")).toBeInTheDocument())
    view.unmount()
  })
})
