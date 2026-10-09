import { render, screen } from "@testing-library/react"
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
      appName="EDIS"
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

    expect(screen.getByRole("img", { name: "EDIS" }).getAttribute("src")).toBe(expected)
  })

  it("falls back to the platform's shield, not the platform's logo, without any application logo", () => {
    theme.resolved = "dark"
    renderLayout({ light: null, dark: null })

    expect(screen.queryByRole("img", { name: "EDIS" })).toBeNull()
    expect(screen.queryByTestId("platform-mark")).toBeNull()
  })
})
