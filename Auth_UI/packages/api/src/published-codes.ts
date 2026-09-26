import type { PublishedErrorCode } from "@authsystem/api/error-codes.generated"

/**
 * Where a published code comes from ("catalog", "transport", "challenge"), or,
 * for a Validation code that names one, the pointer of its body member.
 */
export type PublishedOrigin = "catalog" | "transport" | "challenge" | `#/${string}`

let published: Readonly<Record<string, PublishedOrigin>> | undefined

/**
 * Loads the published code map (`error-codes.generated.ts`, ADR 0001).
 *
 * It is some 20 kB that only a failure needs, so it is a chunk of its own rather
 * than part of every entry: `readProblem` awaits it before any code in a response
 * is judged, and a session in which every request succeeds never downloads it.
 *
 * A load that fails - offline, or a chunk a newer deployment removed and a
 * static host answers with HTML - leaves every code unknown, so the failure reads
 * by its transport status with local copy. Closed, not open; and the next
 * failure tries again, because a rejected load is not remembered.
 */
export async function loadPublishedErrorCodes(): Promise<void> {
  if (published) return
  try {
    published = (await import("@authsystem/api/error-codes.generated")).PUBLISHED_ERROR_CODES
  } catch {
    // Fail closed: see above.
  }
}

/** The origin of a published code, or undefined for anything else - and for everything before the map has loaded. */
export function publishedOrigin(code: unknown): PublishedOrigin | undefined {
  return typeof code === "string" && published && Object.hasOwn(published, code)
    ? published[code]
    : undefined
}

export function isPublishedCode(code: unknown): code is PublishedErrorCode {
  return publishedOrigin(code) !== undefined
}
