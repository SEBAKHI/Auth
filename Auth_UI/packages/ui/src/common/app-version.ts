export function normalizeAppEntryUrl(value: string, origin: string): string {
  const url = new URL(value, origin)
  return `${url.origin}${url.pathname}${url.search}`
}

/**
 * The fingerprinted module entry this document is currently running.
 *
 * Doubles as a deployment identity: it changes on every build, so "am I still on
 * the entry I already tried to recover from?" is answerable without a counter.
 */
export function currentModuleEntry(origin: string): string | null {
  const source = document.querySelector<HTMLScriptElement>(
    'script[type="module"][src]'
  )?.src
  return source ? normalizeAppEntryUrl(source, origin) : null
}

/**
 * Same-page navigation carrying a cache-busting parameter, so an intermediary
 * holding the old shell cannot answer the reload with the same stale document.
 */
export function reloadWithCacheBust(): void {
  const url = new URL(window.location.href)
  url.searchParams.set("__app_update", Date.now().toString())
  window.location.replace(url.href)
}

/** Removes the cache-busting parameter once the reload has landed. */
export function clearCacheBustParameter(): void {
  const url = new URL(window.location.href)
  if (!url.searchParams.has("__app_update")) return
  url.searchParams.delete("__app_update")
  window.history.replaceState(window.history.state, "", url.href)
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
