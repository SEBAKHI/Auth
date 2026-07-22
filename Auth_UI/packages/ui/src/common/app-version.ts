export function normalizeAppEntryUrl(value: string, origin: string): string {
  const url = new URL(value, origin)
  return `${url.origin}${url.pathname}${url.search}`
}

/** Returns the JavaScript entry referenced by a Vite index document. */
export function moduleEntryFromHtml(
  html: string,
  origin: string
): string | null {
  const parsed = new DOMParser().parseFromString(html, "text/html")
  const source = parsed.querySelector<HTMLScriptElement>(
    'script[type="module"][src]'
  )?.src
  return source ? normalizeAppEntryUrl(source, origin) : null
}

/**
 * Checks the uncached SPA shell and confirms its referenced bundle exists
 * before reporting an update. Network failures leave the current app running.
 */
export async function detectAvailableAppUpdate(
  currentEntry: string,
  origin: string,
  fetcher: typeof fetch = fetch
): Promise<string | null> {
  try {
    const indexUrl = new URL("/index.html", origin)
    indexUrl.searchParams.set("__app_check", Date.now().toString())

    const indexResponse = await fetcher(indexUrl, {
      cache: "no-store",
      credentials: "same-origin",
    })
    if (!indexResponse.ok) return null

    const latestEntry = moduleEntryFromHtml(await indexResponse.text(), origin)
    if (!latestEntry || latestEntry === currentEntry) return null

    const assetResponse = await fetcher(latestEntry, {
      method: "HEAD",
      cache: "no-store",
      credentials: "same-origin",
    })
    const contentType = assetResponse.headers.get("content-type") ?? ""
    return assetResponse.ok && contentType.includes("javascript")
      ? latestEntry
      : null
  } catch {
    return null
  }
}
