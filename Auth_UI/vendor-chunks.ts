/**
 * Vendor chunking shared by both apps.
 *
 * Both shipped as one chunk per app before this, so the login screen carried
 * recharts, CodeMirror, react-day-picker and qrcode. Route-level splitting handles
 * the app code; this splits the libraries those routes pull in, so a chunk is
 * fetched only by the route that actually needs it.
 *
 * A package a given app does not use simply never matches — the same mapping is
 * safe for both.
 */

/** The installed package a module id belongs to, or null for first-party code. */
function packageOf(id: string): string | null {
  const normalized = id.replace(/\\/g, "/")
  const marker = normalized.lastIndexOf("node_modules/")
  if (marker < 0) return null

  const rest = normalized.slice(marker + "node_modules/".length)
  const parts = rest.split("/")
  // pnpm nests real packages under a virtual store dir; the name is the segment
  // after the second `node_modules/`, which `lastIndexOf` above already selects.
  return parts[0]?.startsWith("@") ? `${parts[0]}/${parts[1]}` : (parts[0] ?? null)
}

/**
 * Only genuinely app-wide vendors are grouped, and only by their own package name.
 *
 * Two things learned the hard way here. Sweeping in transitive deps (`fast-equals`,
 * `eventemitter3`, `style-mod`, the `d3-*` family) puts them in the same chunk as
 * their parent, and because eager code also uses some of them the *whole* chunk
 * became a static dependency of the entry — recharts and CodeMirror ended up
 * `modulepreload`ed on the login screen, the exact opposite of the intent. And
 * recharts/CodeMirror need no group at all: only lazy routes import them, so
 * route-level splitting already isolates them.
 *
 * Most specific first: `react` would otherwise swallow `react-i18next`.
 */
const GROUPS: Array<[chunk: string, packages: RegExp]> = [
  // `react-query` is eager (the providers wrap the whole app) but `react-table` is
  // only reached from list pages, which are lazy — grouping them together would
  // have pulled the table library into the initial load.
  ["react-query", /^@tanstack\/(react-)?query.*$/],
  ["i18n", /^(i18next|react-i18next)$/],
  ["react", /^(react|react-dom|react-router|react-router-dom|scheduler)$/],
]

export function vendorChunk(id: string): string | undefined {
  const pkg = packageOf(id)
  if (!pkg) return undefined
  for (const [chunk, pattern] of GROUPS) {
    if (pattern.test(pkg)) return chunk
  }
  return undefined
}
