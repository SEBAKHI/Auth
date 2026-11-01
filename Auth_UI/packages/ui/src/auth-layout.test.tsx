import { act, fireEvent, render, screen, within } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const theme = vi.hoisted(() => ({ resolved: "light" as "light" | "dark" }))
const branding = vi.hoisted(() => ({
  loginDesign: undefined as { id: string; contentHash: string; showPlatformLogo: boolean } | null | undefined,
  logoHeightPx: 60,
}))
const panels = vi.hoisted(() => ({
  rendered: [] as { clientId: string | null; design: { id: string } }[],
  report: null as null | ((state: "pending" | "rendered" | "failed") => void),
}))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useResolvedTheme: () => theme.resolved,
}))
// The platform's logo is real here, named by the platform: the corner's mark is found by
// that name, and its box read off the element the page draws.
vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({
    loginDesign: branding.loginDesign,
    logoUrl: "platform.webp",
    logoHeightPx: branding.logoHeightPx,
    name: "Auth",
    isPending: false,
  }),
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

/** The platform's mark in the corner, or null. */
const platformMark = () => within(screen.getByRole("banner")).queryByRole("img", { name: "Auth" })
/** The height a logo's box is drawn at, as its own variable says. */
const boxHeight = (element: HTMLElement) => element.style.getPropertyValue("--logo-h")

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
    expect(platformMark()).toBeInTheDocument()
  })
})

/**
 * Which logo stands where: each logo has one place, beside the words that name it, so the
 * application's and the platform's cannot be taken for each other. The platform's mark is in
 * the corner, the application's logo above the title. A design that hides the platform's mark
 * hides that mark alone and moves nothing (owner, 2026-10-31, reversing the corner rule of
 * 2026-10-28).
 */
describe("AuthLayout — each logo in its one place", () => {
  const banner = () => within(screen.getByRole("banner"))
  const main = () => within(screen.getByRole("main"))

  it("shows the platform's mark in the corner on the platform's own pages, and nothing else", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" subtitle="Access the console." securedBy="Secured by Auth">
        <div />
      </AuthLayout>
    )

    expect(platformMark()).toBeInTheDocument()
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

    expect(platformMark()).toBeInTheDocument()
    expect(banner().queryByRole("img", { name: "SHOP" })).toBeNull()
    expect(main().getByRole("img", { name: "SHOP" })).toHaveAttribute("src", "shop.webp")
    expect(main().getByText("Continue to SHOP.")).toBeInTheDocument()
    expect(main().getByText("Secured by Auth")).toBeInTheDocument()
  })

  it("hides only the platform's mark when its design hides it: the application's logo stays above the title", () => {
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

    expect(platformMark()).toBeNull()
    // Nothing moves into the corner.
    expect(banner().queryByRole("img")).toBeNull()
    const logo = main().getByRole("img", { name: "SHOP" })
    expect(logo).toHaveAttribute("src", "shop.webp")
    // At its own size, the same as with the platform's mark shown.
    expect(boxHeight(logo)).toBe("48px")
    expect(screen.getAllByRole("img", { name: "SHOP" })).toHaveLength(1)
    expect(main().getByText("Continue to SHOP.")).toBeInTheDocument()
    // The platform stays named even with its mark hidden.
    expect(main().getByText("Secured by Auth")).toBeInTheDocument()
  })

  it("names the application above the title when its design hides the platform's mark and it has no logo", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" appName="SHOP" showPlatformLogo={false}>
        <div />
      </AuthLayout>
    )

    expect(platformMark()).toBeNull()
    expect(banner().queryByText("SHOP")).toBeNull()
    expect(main().getByText("SHOP")).toBeInTheDocument()
  })

  it("never hides the platform's mark on the platform's own pages", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" showPlatformLogo={false}>
        <div />
      </AuthLayout>
    )

    expect(platformMark()).toBeInTheDocument()
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

  it("names the application above the title when its logo fails to load and the platform's mark is hidden", () => {
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="gone.webp" showPlatformLogo={false}>
        <div />
      </AuthLayout>
    )

    fireEvent.error(main().getByRole("img", { name: "SHOP" }))

    expect(screen.queryByRole("img", { name: "SHOP" })).toBeNull()
    expect(banner().queryByText("SHOP")).toBeNull()
    expect(main().getByText("SHOP")).toBeInTheDocument()
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

  it("hides only the platform's mark when the application's design hides it", () => {
    branding.loginDesign = PLATFORM
    wide(false)
    theme.resolved = "light"
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="shop.webp" loginDesign={{ clientId: "shop", design: OWN }}>
        <div />
      </AuthLayout>
    )

    expect(platformMark()).toBeNull()
    expect(within(screen.getByRole("banner")).queryByRole("img")).toBeNull()
    expect(within(screen.getByRole("main")).getByRole("img", { name: "SHOP" })).toBeInTheDocument()
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

/**
 * The sizes (owner, 2026-10-31): the platform's mark defaults to 60px (1.5 × the former 40px),
 * its width cap grows with it, and it no longer shrinks when an application is named; each logo
 * takes the height its owner chose (D5), the application's defaulting to its 48px. Breaks: bring
 * back the `compact` 28px mark; draw the application's logo at a fixed height; size the width cap
 * apart from the height.
 */
describe("AuthLayout — the logos' heights", () => {
  it("draws the platform's mark at 60px with a 240px width cap by default", () => {
    branding.loginDesign = null
    branding.logoHeightPx = 60
    render(
      <AuthLayout title="Sign in">
        <div />
      </AuthLayout>
    )

    const mark = platformMark()!
    expect(boxHeight(mark)).toBe("60px")
    expect(mark.style.getPropertyValue("--logo-w-max")).toBe("240px")
    expect(mark).toHaveClass("object-contain", "w-auto")
  })

  it("keeps the platform's mark at its size when an application is named", () => {
    branding.loginDesign = null
    branding.logoHeightPx = 60
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="shop.webp" continueTo="Continue to SHOP.">
        <div />
      </AuthLayout>
    )

    expect(boxHeight(platformMark()!)).toBe("60px")
  })

  it("draws each logo at the height its owner chose, and the width cap follows", () => {
    branding.loginDesign = null
    branding.logoHeightPx = 110
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="shop.webp" appLogoHeightPx={96}>
        <div />
      </AuthLayout>
    )

    expect(boxHeight(platformMark()!)).toBe("110px")
    expect(platformMark()!.style.getPropertyValue("--logo-w-max")).toBe("440px")
    const logo = within(screen.getByRole("main")).getByRole("img", { name: "SHOP" })
    expect(boxHeight(logo)).toBe("96px")
    // An application's cap is 5 × its height, the 240×48 it always had.
    expect(logo.style.getPropertyValue("--logo-w-max")).toBe("480px")
  })

  it.each([
    [null, "48px"],
    [undefined, "48px"],
    [200, "48px"],
    [23, "48px"],
    [24, "24px"],
    [120, "120px"],
  ])("draws the application's logo at %s → %s", (stored, expected) => {
    branding.loginDesign = null
    branding.logoHeightPx = 60
    render(
      <AuthLayout title="Sign in" appName="SHOP" appLogoUrl="shop.webp" appLogoHeightPx={stored}>
        <div />
      </AuthLayout>
    )

    expect(boxHeight(within(screen.getByRole("main")).getByRole("img", { name: "SHOP" }))).toBe(expected)
  })
})
