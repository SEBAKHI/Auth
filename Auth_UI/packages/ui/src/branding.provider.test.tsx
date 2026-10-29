import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import {
  BrandingLogo,
  BrandingProvider,
  useApplicationTheme,
  useBranding,
  useThemePreview,
} from "./branding"
import {
  DEFAULT_THEME_CONFIG,
  THEME_ENGINE_VERSION,
  themeConfigKey,
  toThemeConfig,
} from "./theme/theme-config"

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

    resolve({ data: { platformName: "YourBrand", logoUrl: "/uploads/logo.webp" } })

    await waitFor(() =>
      expect(screen.getByTestId("name")).toHaveTextContent("YourBrand")
    )
    expect(document.title).toBe("YourBrand")
    expect(screen.getByRole("img")).toHaveAttribute(
      "src",
      "https://api.test/uploads/logo.webp"
    )
  })

  it("shows the default mark once the answer says there is no logo", async () => {
    stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "YourBrand", logoUrl: null } })

    renderProvider()

    await waitFor(() =>
      expect(screen.getByText("default-shield")).toBeInTheDocument()
    )
  })

  it("paints the cached brand on the first frame for a returning visitor", () => {
    stubLocalStorage({
      [BRANDING_CACHE_KEY]: JSON.stringify({
        platformName: "YourBrand",
        logoUrl: "/uploads/logo.webp",
      }),
    })
    get.mockReturnValue(new Promise(() => {}))

    renderProvider()

    expect(screen.getByTestId("pending")).toHaveTextContent("false")
    expect(screen.getByTestId("name")).toHaveTextContent("YourBrand")
  })

  it("stores each answer so the next visit has one to paint", async () => {
    const entries = stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "YourBrand", logoUrl: null } })

    renderProvider()

    await waitFor(() =>
      expect(entries.get(BRANDING_CACHE_KEY)).toContain("YourBrand")
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

/**
 * OI-165: whether the sign-in page offers "Stay signed in". The server applies
 * the administrator's choice whatever the page shows, so the page only has to
 * avoid a box that flashes in and out or moves the button: unknown (null) while
 * nothing is known, shown when the answer is missing or failed with no cached
 * copy, as before the setting existed.
 */
describe("BrandingProvider — staySignedInOffered", () => {
  function OfferedProbe() {
    const { staySignedInOffered, isPending } = useBranding()
    return (
      <>
        <span data-testid="offered">{String(staySignedInOffered)}</span>
        <span data-testid="offered-pending">{String(isPending)}</span>
      </>
    )
  }

  function renderOffered() {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <QueryClientProvider client={client}>
        <BrandingProvider>
          <OfferedProbe />
        </BrandingProvider>
      </QueryClientProvider>
    )
  }

  beforeEach(() => {
    get.mockReset()
  })

  it.each([
    [true, "true"],
    [false, "false"],
  ])("follows the payload's staySignedInOffered: %s", async (offered, shown) => {
    stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "YourBrand", staySignedInOffered: offered } })

    renderOffered()

    // The answer must have landed: the unknown state reads "null", not "false".
    await waitFor(() => expect(screen.getByTestId("offered-pending")).toHaveTextContent("false"))
    expect(screen.getByTestId("offered")).toHaveTextContent(shown)
  })

  it("offers the box when the payload has no such field (an older API)", async () => {
    stubLocalStorage()
    get.mockResolvedValue({ data: { platformName: "YourBrand" } })

    renderOffered()

    await waitFor(() => expect(screen.getByTestId("offered")).toHaveTextContent("true"))
  })

  it("offers the box when the branding request fails", async () => {
    stubLocalStorage()
    get.mockRejectedValue(new Error("network"))

    renderOffered()

    await waitFor(() => expect(screen.getByTestId("offered")).toHaveTextContent("true"))
  })

  it("is unknown (null) while nothing is known yet", () => {
    stubLocalStorage()
    get.mockReturnValue(new Promise(() => {}))

    renderOffered()

    expect(screen.getByTestId("offered")).toHaveTextContent("null")
  })

  it("keeps a cached false when the refetch fails", async () => {
    stubLocalStorage({
      [BRANDING_CACHE_KEY]: JSON.stringify({ platformName: "YourBrand", staySignedInOffered: false }),
    })
    get.mockRejectedValue(new Error("network"))

    renderOffered()

    await waitFor(() => expect(get).toHaveBeenCalled())
    expect(screen.getByTestId("offered")).toHaveTextContent("false")
  })

  it("answers from a cached copy first, then from the refetch", async () => {
    stubLocalStorage({
      [BRANDING_CACHE_KEY]: JSON.stringify({ platformName: "YourBrand", staySignedInOffered: true }),
    })
    let resolve!: (value: { data: unknown }) => void
    get.mockReturnValue(new Promise<{ data: unknown }>((r) => (resolve = r)))

    renderOffered()
    expect(screen.getByTestId("offered")).toHaveTextContent("true")

    await act(async () => resolve({ data: { platformName: "YourBrand", staySignedInOffered: false } }))
    await waitFor(() => expect(screen.getByTestId("offered")).toHaveTextContent("false"))
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
        key: `${THEME_ENGINE_VERSION}|${themeConfigKey(toThemeConfig(blue))}`,
        css: "html:root{--primary:oklch(0.1 0.1 1);}html.dark{}",
      }),
    })
    get.mockResolvedValue({ data: { platformName: "Acme", theme: blue } })

    renderProvider()

    await waitFor(() => expect(sheet()).toBe("html:root{--primary:oklch(0.1 0.1 1);}html.dark{}"))
  })

  it("recomputes a stylesheet cached by an older engine, even for the same appearance", async () => {
    const storage = stubLocalStorage({
      [THEME_CSS_KEY]: JSON.stringify({
        key: `${THEME_ENGINE_VERSION - 1}|${themeConfigKey(toThemeConfig(blue))}`,
        css: "html:root{--primary:oklch(0.1 0.1 1);}html.dark{}",
      }),
    })
    get.mockResolvedValue({ data: { platformName: "Acme", theme: blue } })

    renderProvider()

    await waitFor(() => expect(sheet()).toContain("--primary:oklch(0.488 0.243 264.376);"))
    expect(JSON.parse(storage.get(THEME_CSS_KEY)!).key.startsWith(`${THEME_ENGINE_VERSION}|`)).toBe(true)
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

describe("BrandingProvider — an application's own appearance", () => {
  const PLATFORM_KEY = "auth.ui.theme-css"
  const APP_KEY = "auth.ui.theme-css:app:crm"
  // The platform: Blue, large corners. The application: Violet, small corners.
  const platform = {
    base: { preset: "neutral" },
    theme: { preset: "blue" },
    chart: { preset: "cyan" },
    radius: "large",
    menuAccent: "subtle",
  }
  const crm = toThemeConfig({ ...platform, theme: { preset: "violet" }, radius: "small" })
  const PLATFORM_RADIUS = "--radius:0.875rem;"
  const APP_RADIUS = "--radius:0.45rem;"

  beforeEach(() => {
    document.head.innerHTML = '<link rel="icon" type="image/svg+xml" href="/vite.svg" />'
    get.mockReset()
    get.mockResolvedValue({ data: { platformName: "Acme", theme: platform } })
  })

  afterEach(() => {
    document.head.innerHTML = ""
  })

  const sheet = () => document.getElementById("platform-theme")?.textContent ?? null

  /** A page of the application's flow, as `useFlowBranding` registers it. */
  function FlowPage({ theme }: { theme: ReturnType<typeof toThemeConfig> | null | undefined }) {
    useApplicationTheme("crm", theme)
    return null
  }

  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const tree = (page: React.ReactNode, applicationScope?: string | null) => (
    <QueryClientProvider client={client}>
      <BrandingProvider applicationScope={applicationScope}>{page}</BrandingProvider>
    </QueryClientProvider>
  )

  function renderWith(page: React.ReactNode, applicationScope?: string | null) {
    client.clear()
    return render(tree(page, applicationScope))
  }

  it("replaces the platform's on a page of the application's flow, and caches it under the application's key", async () => {
    const storage = stubLocalStorage()

    renderWith(<FlowPage theme={crm} />, null)

    await waitFor(() => expect(sheet()).toContain(APP_RADIUS))
    expect(sheet()).not.toContain(PLATFORM_RADIUS)
    expect(JSON.parse(storage.get(APP_KEY)!).css).toBe(sheet())
    expect(storage.has(PLATFORM_KEY)).toBe(false)
  })

  it("never applies in the console, which takes no applications' appearances", async () => {
    const storage = stubLocalStorage()

    renderWith(<FlowPage theme={crm} />)

    await waitFor(() => expect(sheet()).toContain(PLATFORM_RADIUS))
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(sheet()).not.toContain(APP_RADIUS)
    expect(storage.has(APP_KEY)).toBe(false)
  })

  it("shows the platform's when the application's is off, and forgets the application's cached one", async () => {
    const storage = stubLocalStorage({ [APP_KEY]: JSON.stringify({ key: "old", css: "html:root{--radius:0.45rem;}" }) })

    renderWith(<FlowPage theme={null} />, "crm")

    await waitFor(() => expect(sheet()).toContain(PLATFORM_RADIUS))
    expect(storage.has(APP_KEY)).toBe(false)
  })

  it("keeps the application's cached colours while its branding is unknown, though the platform's has answered", async () => {
    const cached = "html:root{--radius:0.45rem;--primary:oklch(0.5 0.2 300);}"
    stubLocalStorage({ [APP_KEY]: JSON.stringify({ key: "cached", css: cached }) })
    const style = document.createElement("style")
    style.id = "platform-theme"
    style.textContent = cached
    document.head.appendChild(style)

    renderWith(<FlowPage theme={undefined} />, "crm")

    // The platform's branding answers; the application's never does.
    await waitFor(() => expect(get).toHaveBeenCalled())
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(sheet()).toBe(cached)
  })

  it("shows the platform's while the application's branding is unknown and nothing of it is cached", async () => {
    stubLocalStorage()

    renderWith(<FlowPage theme={undefined} />, "crm")

    await waitFor(() => expect(sheet()).toContain(PLATFORM_RADIUS))
  })

  it("goes back to the platform's when the flow's page is gone", async () => {
    stubLocalStorage()
    const view = renderWith(<FlowPage theme={crm} />, null)
    await waitFor(() => expect(sheet()).toContain(APP_RADIUS))

    view.rerender(tree(null, null))

    await waitFor(() => expect(sheet()).toContain(PLATFORM_RADIUS))
  })
})

/**
 * The platform's sign-in page design (phase 6): which design the platform's own pages
 * show, unknown while nothing is known, none when the payload says none or the request
 * failed. Break: answer null while pending (the panel would treat "unknown" as "none").
 */
describe("BrandingProvider — the platform's sign-in page design", () => {
  function DesignProbe() {
    const { loginDesign } = useBranding()
    return <span data-testid="design">{loginDesign === undefined ? "unknown" : JSON.stringify(loginDesign)}</span>
  }

  function renderDesign() {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <QueryClientProvider client={client}>
        <BrandingProvider>
          <DesignProbe />
        </BrandingProvider>
      </QueryClientProvider>
    )
  }

  beforeEach(() => {
    get.mockReset()
  })

  it("is unknown while nothing is known yet", () => {
    stubLocalStorage()
    get.mockReturnValue(new Promise(() => {}))
    renderDesign()
    expect(screen.getByTestId("design")).toHaveTextContent("unknown")
  })

  it("is the design the payload names", async () => {
    stubLocalStorage()
    const design = { id: "d1", contentHash: "h1", showPlatformLogo: false }
    get.mockResolvedValue({ data: { platformName: "YourBrand", loginDesign: design } })
    renderDesign()
    await waitFor(() => expect(screen.getByTestId("design")).toHaveTextContent(JSON.stringify(design)))
  })

  it.each([
    ["the payload says none", () => get.mockResolvedValue({ data: { platformName: "YourBrand", loginDesign: null } })],
    ["the payload has no such field (an older API)", () => get.mockResolvedValue({ data: { platformName: "YourBrand" } })],
    ["the request failed", () => get.mockRejectedValue(new Error("network"))],
  ])("is none (the plain panel) when %s", async (_case, arrange) => {
    stubLocalStorage()
    arrange()
    renderDesign()
    await waitFor(() => expect(screen.getByTestId("design")).toHaveTextContent("null"))
  })
})
