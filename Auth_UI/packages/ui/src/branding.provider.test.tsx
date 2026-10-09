import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { BrandingLogo, BrandingProvider, useBranding, useThemePreview } from "./branding"
import { DEFAULT_THEME_CONFIG, themeConfigKey, toThemeConfig } from "./theme/theme-config"

const BRANDING_CACHE_KEY = "auth.ui.branding"

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useTheme: () => ({ resolvedTheme: "light" }),
}))

const get = vi.fn()
vi.mock("@authsystem/api/client", () => ({ api: { GET: (...a: unknown[]) => get(...a) } }))
vi.mock("@authsystem/api/helpers", () => ({
  unwrap: (promise: Promise<{ data: unknown }>) => promise.then((r) => r.data),
}))
vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.test" }))

/** jsdom here has no Storage at all, so the cache path needs a stand-in. */
function stubLocalStorage(seed?: Record<string, string>) {
  const entries = new Map(Object.entries(seed ?? {}))
  Object.defineProperty(window, "localStorage", {
    configurable: true,
    value: {
      getItem: (key: string) => entries.get(key) ?? null,
      setItem: (key: string, value: string) => void entries.set(key, value),
      removeItem: (key: string) => void entries.delete(key),
      clear: () => entries.clear(),
      key: () => null,
      length: 0,
    },
  })
  return entries
}

function Probe() {
  const { name, isPending } = useBranding()
  return (
    <>
      <span data-testid="name">{name}</span>
      <span data-testid="pending">{String(isPending)}</span>
      <BrandingLogo className="logo-box" fallback={<span>default-shield</span>} />
    </>
  )
}

function renderProvider() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <BrandingProvider>
        <Probe />
      </BrandingProvider>
    </QueryClientProvider>
  )
}

describe("BrandingProvider", () => {
  beforeEach(() => {
    document.head.innerHTML =
      '<link rel="icon" type="image/svg+xml" href="/vite.svg" />'
    document.title = "Accounts"
    get.mockReset()
  })

  afterEach(() => {
    document.head.innerHTML = ""
  })

  it("asserts nothing about the brand before the answer arrives", async () => {
    stubLocalStorage()
    let resolve!: (value: { data: unknown }) => void
    get.mockReturnValue(
      new Promise<{ data: unknown }>((r) => {
        resolve = r
      })
    )

    renderProvider()

    // The whole defect in one assertion: no default shield, and the tab keeps
    // the document's own title rather than the compiled-in product name.
    expect(screen.queryByText("default-shield")).not.toBeInTheDocument()
    expect(document.title).toBe("Accounts")
    expect(screen.getByTestId("pending")).toHaveTextContent("true")

    resolve({ data: { platformName: "SEBAKHI", logoUrl: "/uploads/logo.webp" } })

    await waitFor(() =>
      expect(screen.getByTestId("name")).toHaveTextContent("SEBAKHI")
    )
    expect(document.title).toBe("SEBAKHI")
    expect(screen.getByRole("img")).toHaveAttribute(
      "src",
      "https://api.test/uploads/logo.webp"
    )
  })

  it("shows the default mark once the answer says there is no logo", async () => {
    stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "SEBAKHI", logoUrl: null } })

    renderProvider()

    await waitFor(() =>
      expect(screen.getByText("default-shield")).toBeInTheDocument()
    )
  })

  it("paints the cached brand on the first frame for a returning visitor", () => {
    stubLocalStorage({
      [BRANDING_CACHE_KEY]: JSON.stringify({
        platformName: "SEBAKHI",
        logoUrl: "/uploads/logo.webp",
      }),
    })
    get.mockReturnValue(new Promise(() => {}))

    renderProvider()

    expect(screen.getByTestId("pending")).toHaveTextContent("false")
    expect(screen.getByTestId("name")).toHaveTextContent("SEBAKHI")
  })

  it("stores each answer so the next visit has one to paint", async () => {
    const entries = stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "SEBAKHI", logoUrl: null } })

    renderProvider()

    await waitFor(() =>
      expect(entries.get(BRANDING_CACHE_KEY)).toContain("SEBAKHI")
    )
  })

  it("revalidates a cached brand instead of trusting it for the stale window", async () => {
    stubLocalStorage({
      [BRANDING_CACHE_KEY]: JSON.stringify({ platformName: "Old Name" }),
    })
    get.mockResolvedValue({ data: { platformName: "New Name" } })

    renderProvider()

    // Seeded as already-stale, so the cache buys a first frame — never silence.
    await waitFor(() =>
      expect(screen.getByTestId("name")).toHaveTextContent("New Name")
    )
    expect(get).toHaveBeenCalled()
  })
})

describe("BrandingProvider — the platform appearance", () => {
  const THEME_CSS_KEY = "auth.ui.theme-css"
  const blue = {
    base: { preset: "neutral" },
    theme: { preset: "blue" },
    chart: { preset: "cyan" },
    radius: "large",
    menuAccent: "subtle",
  }

  beforeEach(() => {
    document.head.innerHTML = '<link rel="icon" type="image/svg+xml" href="/vite.svg" />'
    get.mockReset()
  })

  afterEach(() => {
    document.head.innerHTML = ""
  })

  const sheet = () => document.getElementById("platform-theme")?.textContent ?? null

  it("applies the saved appearance and caches the stylesheet for the next first frame", async () => {
    const storage = stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "Acme", theme: blue } })

    renderProvider()

    await waitFor(() => expect(sheet()).toContain("--radius:0.875rem;"))
    // Blue's light primary, verbatim from shadcn's registry.
    expect(sheet()).toContain("--primary:oklch(0.488 0.243 264.376);")
    expect(JSON.parse(storage.get(THEME_CSS_KEY)!).css).toBe(sheet())
  })

  it("reuses a stylesheet computed for the same appearance instead of loading the registry", async () => {
    // The cached css is deliberately not what the registry would produce: it
    // can only end up on the page if nothing was recomputed.
    stubLocalStorage({
      [THEME_CSS_KEY]: JSON.stringify({
        key: themeConfigKey(toThemeConfig(blue)),
        css: "html:root{--primary:oklch(0.1 0.1 1);}html.dark{}",
      }),
    })
    get.mockResolvedValue({ data: { platformName: "Acme", theme: blue } })

    renderProvider()

    await waitFor(() => expect(sheet()).toBe("html:root{--primary:oklch(0.1 0.1 1);}html.dark{}"))
  })

  it("draws the shipped preset with preset.css alone, and forgets an old appearance", async () => {
    const storage = stubLocalStorage({
      [THEME_CSS_KEY]: JSON.stringify({ key: "old", css: "html:root{--primary:red;}" }),
    })
    const style = document.createElement("style")
    style.id = "platform-theme"
    style.textContent = "html:root{--primary:red;}"
    document.head.appendChild(style)
    get.mockResolvedValue({ data: { platformName: "Acme", theme: DEFAULT_THEME_CONFIG } })

    renderProvider()

    await waitFor(() => expect(sheet()).toBeNull())
    expect(storage.has(THEME_CSS_KEY)).toBe(false)
  })

  it("leaves a returning visitor's cached colours alone until the branding answers", async () => {
    stubLocalStorage({
      [THEME_CSS_KEY]: JSON.stringify({
        key: "cached",
        css: "html:root{--primary:oklch(0.5 0.1 20);}html.dark{}",
      }),
    })
    get.mockReturnValue(new Promise(() => {}))
    const style = document.createElement("style")
    style.id = "platform-theme"
    style.textContent = "html:root{--primary:oklch(0.5 0.1 20);}html.dark{}"
    document.head.appendChild(style)

    renderProvider()

    // Nothing replaces it with the shipped preset while the answer is unknown.
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(sheet()).toBe("html:root{--primary:oklch(0.5 0.1 20);}html.dark{}")
  })

  it("previews an unsaved appearance without caching it, and restores the saved one", async () => {
    const storage = stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "Acme", theme: blue } })
    function PreviewProbe() {
      const { savedTheme, setThemePreview } = useThemePreview()
      return (
        <>
          <button onClick={() => setThemePreview({ ...savedTheme, radius: "none" })}>
            preview
          </button>
          <button onClick={() => setThemePreview(null)}>clear</button>
        </>
      )
    }
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <BrandingProvider>
          <PreviewProbe />
        </BrandingProvider>
      </QueryClientProvider>
    )
    await waitFor(() => expect(sheet()).toContain("264.376"))
    const saved = storage.get(THEME_CSS_KEY)

    act(() => screen.getByRole("button", { name: "preview" }).click())
    await waitFor(() => expect(sheet()).toContain("--radius:0;"))
    expect(storage.get(THEME_CSS_KEY)).toBe(saved)

    act(() => screen.getByRole("button", { name: "clear" }).click())
    await waitFor(() => expect(sheet()).toContain("--radius:0.875rem;"))
  })
})
