import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { BrandingLogo, BrandingProvider, useBranding } from "./branding"

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
