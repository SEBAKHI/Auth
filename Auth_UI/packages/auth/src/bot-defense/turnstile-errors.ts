/**
 * Whether trying again can help, per Turnstile error code: the "Retry" column
 * of Cloudflare's client-side error table (S13, ARR-0412), never a hand-made
 * list. A code that says "no" is shown in an Alert with the ways out; one that
 * says "yes" is retried by the widget itself.
 *
 * Source: developers.cloudflare.com/turnstile/troubleshooting/client-side-errors/error-codes/
 * (read 2026-11-12). Codes there written with a `*` keep their prefix here.
 */
const RETRY_BY_CODE: ReadonlyMap<string, boolean> = new Map([
  ["110100", false], // invalid sitekey
  ["110110", false], // sitekey not found
  ["110200", false], // domain not authorized
  ["110600", true], // challenge timed out
  ["110620", true], // interaction timed out
  ["200100", false], // clock or cache problem
  ["200500", true], // iframe load error
  ["400020", false], // invalid sitekey
  ["400021", false], // sitekey domain mismatch
  ["400070", false], // sitekey disabled
])

const RETRY_BY_PREFIX: ReadonlyArray<readonly [string, boolean]> = [
  ["300", true], // generic challenge failure
  ["600", true], // generic challenge failure
]

/**
 * False when Cloudflare says trying again cannot help. A code the table does
 * not list is treated as retryable: the widget's own retry runs, and a person
 * who still has no token is told to complete the check.
 */
export function isRetryableTurnstileError(code: string): boolean {
  const exact = RETRY_BY_CODE.get(code)
  if (exact !== undefined) return exact
  const prefix = RETRY_BY_PREFIX.find(([start]) => code.startsWith(start))
  return prefix ? prefix[1] : true
}

/**
 * The widget's language for the page's language: Turnstile speaks every app
 * language but Urdu, which falls back to English (BD9). Simplified Chinese is
 * `zh-cn` in Cloudflare's list.
 */
const LANGUAGE_BY_APP_LANGUAGE: ReadonlyMap<string, string> = new Map([
  ["en", "en"],
  ["ar", "ar"],
  ["fa", "fa"],
  ["fr", "fr"],
  ["tr", "tr"],
  ["zh", "zh-cn"],
  ["ur", "en"],
])

export function turnstileLanguage(appLanguage: string): string {
  const base = appLanguage.toLowerCase().split("-")[0]
  return LANGUAGE_BY_APP_LANGUAGE.get(base) ?? "en"
}
