/**
 * The tab icon's address: one PNG the API keeps at a stable key, rebuilt whenever the
 * administrator saves the favicon (or the logo), and holding the product's default mark when
 * there is neither (`Auth.Infrastructure/Services/FileSystemImageStorageService.TabIcon.cs`).
 *
 * Both apps' HTML names it (`favicon-link.ts`), so the browser shows the platform's icon from
 * its first parse of the page. A script that swapped the icon in later showed the template's
 * icon on every load, and Safari, which does not reliably follow a swapped icon, kept it.
 *
 * Uploads are served by the API under this path (`ImageStorage:PublicBaseUrl`), as the
 * policies in both apps' web.config already assume for every uploaded image.
 */
export const PLATFORM_FAVICON_PATH = "/uploads/images/platform-favicon.png"

/** The tab icon's absolute address for an API base URL; null when the base is not absolute http(s). */
export function platformFaviconUrl(apiBaseUrl: unknown): string | null {
  if (typeof apiBaseUrl !== "string" || !apiBaseUrl) return null
  try {
    const url = new URL(apiBaseUrl)
    if (url.protocol !== "https:" && url.protocol !== "http:") return null
  } catch {
    return null
  }
  return `${apiBaseUrl.replace(/\/+$/, "")}${PLATFORM_FAVICON_PATH}`
}
