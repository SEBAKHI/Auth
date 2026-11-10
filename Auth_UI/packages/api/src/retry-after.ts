/**
 * The wait a 429 asked for, in whole seconds rounded up, from Retry-After as a
 * number or as a date; null when absent or unreadable.
 *
 * The header is the only place the wait travels (ADR 0001): a 429 has no body
 * that names it. The gateway exposes the header to the apps' origins; a call
 * made straight to the API cannot read it, and then the caller says "wait a
 * moment" without a number.
 */
export function retryAfterSeconds(response: Response): number | null {
  const value = response.headers.get("Retry-After")?.trim()
  if (!value) return null
  if (/^\d+$/.test(value)) return Number(value)
  const at = Date.parse(value)
  return Number.isNaN(at) ? null : Math.max(0, Math.ceil((at - Date.now()) / 1000))
}
