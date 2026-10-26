/**
 * The browser's zone, when it is one the server will accept. The validator
 * takes IANA identifiers only (a slash, or the literal UTC), and a refused zone
 * would fail the whole completion at validation, before the code is even read.
 * An odd runtime value therefore sends nothing, and the profile keeps its
 * "automatic" default.
 */
export function detectedTimeZone(): string | undefined {
  try {
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone
    return zone && zone.includes("/") && zone.length <= 50 ? zone : undefined
  } catch {
    return undefined
  }
}
