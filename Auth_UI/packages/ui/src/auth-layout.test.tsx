import { act, fireEvent, render, screen, within } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const theme = vi.hoisted(() => ({ resolved: "light" as "light" | "dark" }))
const branding = vi.hoisted(() => ({
  loginDesign: undefined as { id: string; contentHash: string; showPlatformLogo: boolean } | null | undefined,
}))
const panels = vi.hoisted(() => ({
  rendered: [] as { clientId: string | null; design: { id: string } }[],
  report: null as null | ((state: "pending" | "rendered" | "failed") => void),
}))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useResolvedTheme: () => theme.resolved,
}))
vi.mock("@authsystem/ui/branding", () => ({
  BrandingLogo: () => <span data-testid="platform-mark" />,
  useBranding: () => ({ loginDesign: branding.loginDesign }),
}))
// The panel's own chunk: here it records what it was asked to show and hands the test
// the way to say "rendered".
vi.mock("@authsystem/ui/login-design/login-design-panel", () => ({
  default: (props: {
    clientId: string | null
    design: { id: string }
    onStateChange: (state: "pending" | "rendered" | "failed") => void
  }) => {
    panels.rendered.push({ clientId: props.clientId, design: props.design })
    panels.report = props.onStateChange
    return <div data-testid="design-panel" />
  },
}))
vi.mock("@authsystem/ui/common/language-toggle", () => ({
  LanguageToggle: () => null,
}))
vi.mock("@authsystem/ui/common/theme-toggle", () => ({
  ThemeToggle: () => null,
}))

import { AuthLayout } from "./auth-layout"

function renderLayout(logos: { light?: string | null; dark?: string | null }) {
  return render(
    <AuthLayout
      title="Sign in"
      appName="SHOP"
      appLogoUrl={logos.light}
      appLogoUrlDark={logos.dark}
    >
      <div />
    </AuthLayout>
  )
}

describe("AuthLayout — the application's logo per theme", () => {
  it.each([
    ["light", { light: "light.webp", dark: "dark.webp" }, "light.webp"],
    ["dark", { light: "light.webp", dark: "dark.webp" }, "dark.webp"],
    // One logo serves both modes, whichever slot it was uploaded to.
    ["dark", { light: "light.webp", dark: null }, "light.webp"],
    ["light", { light: null, dark: "dark.webp" }, "dark.webp"],
  ] as const)("in %s mode shows the matching logo", (mode, logos, expected) => {
    theme.resolved = mode
    renderLayout(logos)

    expect(screen.getByRole("img", { name: "SHOP" }).getAttribute("src")).toBe(expected)
  })

  it("keeps only the platform's mark, in the corner, when the application has no logo", () => {
    theme.resolved = "dark"
    renderLayout({ light: null, dark: null })

    expect(screen.queryByRole("img", { name: "SHOP" })).toBeNull()
    expect(within(screen.getByRole("banner")).getByTestId("platform-mark")).toBeInTheDocument()
  })
})

/**
 * Which logo stands where (owner, 2026-10-28): one large logo per page, each
 * beside the words that name it, so the application's and the platform's
 * cannot be taken for each other.
 */
describe("AuthLayout — one large logo, each beside its name", () => {
  const banner = () => within(screen.getByRole("banner"))
  const main = () => within(screen.getByRole("main"))

  it("shows the platform's mark in the corner on the platform's own pages, and nothing else", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" subtitle="Access the console." securedBy="Secured by Auth">
        <div />
      </AuthLayout>
    )

    expect(banner().getByTestId("platform-mark")).toBeInTheDocument()
    expect(main().queryByRole("img")).toBeNull()
    expect(screen.getByText("Access the console.")).toBeInTheDocument()
    // The trust marker belongs to a page that shows someone else's logo.
    expect(screen.queryByText("Secured by Auth")).toBeNull()
  })

  it("puts the application's logo above the title and names it, with the platform's mark in the corner", () => {
    theme.resolved = "light"
    render(
      <AuthLayout
        title="Sign in"
        appName="SHOP"
        appLogoUrl="shop.webp"
        continueTo="Continue to SHOP."
        securedBy="Secured by Auth"
      >
        <div />
      </AuthLayout>
    )

    expect(banner().getByTestId("platform-mark")).toBeInTheDocument()
    expect(banner().queryByRole("img", { name: "SHOP" })).toBeNull()
    expect(main().getByRole("img", { name: "SHOP" })).toHaveAttribute("src", "shop.webp")
    expect(main().getByText("Continue to SHOP.")).toBeInTheDocument()
    expect(main().getByText("Secured by Auth")).toBeInTheDocument()
  })

  it("moves the application's logo into the corner when its design hides the platform's mark", () => {
    theme.resolved = "light"
    render(
      <AuthLayout
        title="Sign in"
        appName="SHOP"
        appLogoUrl="shop.webp"
        continueTo="Continue to SHOP."
        showPlatformLogo={false}
        securedBy="Secured by Auth"
      >
        <div />
      </AuthLayout>
    )

    expect(screen.queryByTestId("platform-mark")).toBeNull()
    expect(banner().getByRole("img", { name: "SHOP" })).toHaveAttribute("src", "shop.webp")
    // Once, not twice: nothing above the title repeats it.
    expect(screen.getAllByRole("img", { name: "SHOP" })).toHaveLength(1)
    expect(main().getByText("Continue to SHOP.")).toBeInTheDocument()
    // The platform stays named even with its mark hidden.
    expect(main().getByText("Secured by Auth")).toBeInTheDocument()
  })

  it("names the application in the corner when its design hides the platform's mark and it has no logo", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" appName="SHOP" showPlatformLogo={false}>
        <div />
      </AuthLayout>
    )

    expect(screen.queryByTestId("platform-mark")).toBeNull()
    expect(banner().getByText("SHOP")).toBeInTheDocument()
  })

  it("never hides the platform's mark on the platform's own pages", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" showPlatformLogo={false}>
        <div />
      </AuthLayout>
    )

    expect(banner().getByTestId("platform-mark")).toBeInTheDocument()
  })

  it("drops an application logo that fails to load instead of drawing a broken image", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="gone.webp" continueTo="Continue to SHOP.">
        <div />
      </AuthLayout>
    )

    fireEvent.error(main().getByRole("img", { name: "SHOP" }))

    expect(screen.queryByRole("img", { name: "SHOP" })).toBeNull()
    // The application is still named, by the line under the title.
    expect(main().getByText("Continue to SHOP.")).toBeInTheDocument()
  })

  it("names the application in the corner when its logo there fails to load", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="gone.webp" showPlatformLogo={false}>
        <div />
      </AuthLayout>
    )

    fireEvent.error(banner().getByRole("img", { name: "SHOP" }))

    expect(screen.queryByRole("img", { name: "SHOP" })).toBeNull()
    expect(banner().getByText("SHOP")).toBeInTheDocument()
  })

  it("keeps the panel out of the accessibility tree while it is a plain surface", () => {
    const { container } = render(
      <AuthLayout title="Sign in">
        <div />
      </AuthLayout>
    )

    const panel = container.querySelector('[data-slot="auth-panel"]')
    expect(panel).not.toBeNull()
    expect(panel).toHaveAttribute("aria-hidden", "true")
  })
})

/**
 * The panel half: a sign-in page design when one is named (phase 6), loaded only where
 * the panel shows. Deliberate breaks: render the panel whatever the width; take the
 * platform's design on a page of an application's flow; drop `design.showPlatformLogo`.
 */
describe("AuthLayout — the panel's design", () => {
  const PLATFORM = { id: "p1", contentHash: "h1", showPlatformLogo: true }
  const OWN = { id: "a1", contentHash: "h2", showPlatformLogo: false }

  function wide(matches: boolean) {
    vi.spyOn(window, "matchMedia").mockImplementation(
      (query: string) =>
        ({
          media: query,
          matches: query === "(min-width: 1024px)" ? matches : false,
          addEventListener: () => {},
          removeEventListener: () => {},
        }) as unknown as MediaQueryList
    )
  }

  const panel = (container: HTMLElement) => container.querySelector('[data-slot="auth-panel"]')!

  it("shows the platform's design on the platform's own pages, and fetches nothing under lg", async () => {
    branding.loginDesign = PLATFORM
    panels.rendered = []
    wide(false)
    const narrow = render(<AuthLayout title="Sign in"><div /></AuthLayout>)
    // No design is asked for at all under lg (the marker is set with the request).
    expect(panel(narrow.container)).not.toHaveAttribute("data-design-state")
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20))
    })
    expect(narrow.queryByTestId("design-panel")).toBeNull()
    expect(panels.rendered).toEqual([])
    narrow.unmount()

    wide(true)
    const { container, findByTestId } = render(<AuthLayout title="Sign in"><div /></AuthLayout>)
    await findByTestId("design-panel")
    expect(panels.rendered.at(-1)).toEqual({ clientId: null, design: PLATFORM })
    expect(panel(container)).toHaveAttribute("aria-hidden", "true")
    expect(panel(container)).toHaveAttribute("data-design-state", "pending")
  })

  it("counts for assistive technology only once the design has rendered", async () => {
    branding.loginDesign = PLATFORM
    wide(true)
    const { container, findByTestId } = render(<AuthLayout title="Sign in"><div /></AuthLayout>)
    await findByTestId("design-panel")

    await act(async () => panels.report?.("rendered"))

    expect(panel(container)).not.toHaveAttribute("aria-hidden")
    expect(panel(container)).toHaveAttribute("data-design-state", "rendered")
  })

  it("on a page of an application's flow shows the design its branding named, never the platform's", async () => {
    branding.loginDesign = PLATFORM
    panels.rendered = []
    wide(true)
    const { findByTestId } = render(
      <AuthLayout title="Sign in" appName="SHOP" loginDesign={{ clientId: "shop", design: OWN }}>
        <div />
      </AuthLayout>
    )
    await findByTestId("design-panel")
    expect(panels.rendered.at(-1)).toEqual({ clientId: "shop", design: OWN })
  })

  it("shows nothing while the application's design is not known yet, and nothing for none", () => {
    branding.loginDesign = PLATFORM
    wide(true)
    for (const design of [undefined, null]) {
      const { queryByTestId, unmount } = render(
        <AuthLayout title="Sign in" appName="SHOP" loginDesign={{ clientId: "shop", design }}>
          <div />
        </AuthLayout>
      )
      expect(queryByTestId("design-panel")).toBeNull()
      unmount()
    }
  })

  it("gives the corner to the application's logo when its design hides the platform's mark", () => {
    branding.loginDesign = PLATFORM
    wide(false)
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="shop.webp" loginDesign={{ clientId: "shop", design: OWN }}>
        <div />
      </AuthLayout>
    )

    expect(within(screen.getByRole("banner")).getByRole("img", { name: "SHOP" })).toBeInTheDocument()
    expect(within(screen.getByRole("banner")).queryByTestId("platform-mark")).toBeNull()
  })
})

/**
 * Carried from the phase 1 review: on logout and signed-out the application is named by
 * its logo alone (no "Continue to" line). Without a usable logo its name is written where
 * the logo would be. Break: drop `appNamedAsText`.
 */
describe("AuthLayout — an application named without its logo", () => {
  it("writes the name when the logo fails to load and nothing else names the application", () => {
    branding.loginDesign = null
    render(
      <AuthLayout title="You are signed out" appName="Shop Everywhere" appLogoUrl="gone.webp">
        <div />
      </AuthLayout>
    )
    expect(screen.queryByText("Shop Everywhere")).toBeNull()

    fireEvent.error(within(screen.getByRole("main")).getByRole("img", { name: "Shop Everywhere" }))

    expect(within(screen.getByRole("main")).getByText("Shop Everywhere")).toBeInTheDocument()
  })

  it("writes the name when there is no logo at all, and not when the continue line names it", () => {
    branding.loginDesign = null
    const { unmount } = render(
      <AuthLayout title="Signed out" appName="Shop Everywhere">
        <div />
      </AuthLayout>
    )
    expect(within(screen.getByRole("main")).getByText("Shop Everywhere")).toBeInTheDocument()
    unmount()

    render(
      <AuthLayout title="Sign in" appName="Shop Everywhere" continueTo="Continue to Shop Everywhere.">
        <div />
      </AuthLayout>
    )
    expect(within(screen.getByRole("main")).queryByText("Shop Everywhere")).toBeNull()
  })
})
