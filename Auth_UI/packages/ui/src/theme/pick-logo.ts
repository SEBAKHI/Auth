import type { ResolvedTheme } from "@authsystem/ui/theme-provider"

/** Picks the logo for the resolved theme, falling back to the other variant. */
export function pickLogo(
  light: string | null,
  dark: string | null,
  resolvedTheme: ResolvedTheme
): string | null {
  return resolvedTheme === "dark" ? (dark ?? light) : (light ?? dark)
}
