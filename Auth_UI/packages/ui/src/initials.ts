/**
 * Initials for an avatar fallback. Its own module, apart from `format.ts`, so a
 * page that draws an avatar does not download the date library with it: the
 * sign-in page's account list is one (login-payload.spec.ts measures that page).
 */
export function initials(value: string | null | undefined): string {
  if (!value) return "?"
  const parts = value.trim().split(/\s+/).slice(0, 2)
  return parts.map((p) => p.charAt(0).toUpperCase()).join("") || "?"
}
