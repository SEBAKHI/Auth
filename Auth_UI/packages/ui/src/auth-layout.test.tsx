import { fireEvent, render, screen, within } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const theme = vi.hoisted(() => ({ resolved: "light" as "light" | "dark" }))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useResolvedTheme: () => theme.resolved,
}))
vi.mock("@authsystem/ui/branding", () => ({
  BrandingLogo: () => <span data-testid="platform-mark" />,
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
