/**
 * The harness's six HTTPS hosts. All but the attacker share one registrable
 * domain, example.com ("com" is a public suffix and example.com has no rule of
 * its own, so the Public Suffix List's default "*" rule applies), which makes
 * console, accounts, the API and the apex SAME-SITE, and attacker.example.net
 * cross-site. harness-topology.spec.ts proves Chromium agrees on every run.
 *
 * auth.example.com is the API origin both builds bake in: it is the committed
 * placeholder in apps/<app>/.env.production, so neither the bundle nor the sealed
 * CSP changes for the harness.
 *
 * example.com is a real, IANA-operated domain. Nothing reaches it: Chromium
 * sends every https request through proxy.ts as CONNECT and never resolves a
 * name itself, and the proxy refuses any host outside this list. A card that
 * needs another host (a third-party script, say) adds it here and serves it
 * from the test; the per-run certificate covers every host in the list.
 */
export const HOSTS = {
  console: "console.example.com",
  accounts: "accounts.example.com",
  api: "auth.example.com",
  /** Same-site attacker: the registrable domain itself. */
  apex: "example.com",
  /**
   * A linked application on a sibling subdomain, as one that shows the account bar
   * (apps/accounts/public/embed/v1/account-bar.js). Serves only what a test writes.
   */
  linkedApp: "app.example.com",
  /** Cross-site attacker. */
  attacker: "attacker.example.net",
  /**
   * A stand-in for Google's authorization page (S02 PR D): the Google sign-in's popup goes
   * to https://accounts.google.com/o/oauth2/v2/auth, which a test serves (AttackerPages) so
   * the code flow runs end to end with nothing reaching the real Google.
   */
  google: "accounts.google.com",
  /**
   * A stand-in for Cloudflare Turnstile (S13): the bot check loads
   * https://challenges.cloudflare.com/turnstile/v0/api.js and frames a page from the same
   * host, which a test serves (AttackerPages) so the widget runs under the production
   * policies with nothing reaching the real Cloudflare.
   */
  cloudflare: "challenges.cloudflare.com",
} as const

export type HostName = (typeof HOSTS)[keyof typeof HOSTS]

export const ORIGINS = {
  console: `https://${HOSTS.console}`,
  accounts: `https://${HOSTS.accounts}`,
  api: `https://${HOSTS.api}`,
  apex: `https://${HOSTS.apex}`,
  linkedApp: `https://${HOSTS.linkedApp}`,
  attacker: `https://${HOSTS.attacker}`,
  google: `https://${HOSTS.google}`,
  cloudflare: `https://${HOSTS.cloudflare}`,
} as const

export const TOPOLOGY_HOSTS: readonly string[] = Object.values(HOSTS)

/** The API origin the sealed dist-harness/web.config must name (target state 3). */
export const SEALED_API_ORIGIN = ORIGINS.api

/**
 * Origins the API host answers CORS for, with credentials.
 *
 * DELIBERATELY STRICTER TEST THAN PRODUCTION (STAGE-3 ARR-1369/1370): a
 * production CORS list need not contain the apex; it stays here so a harness
 * test can show that a request which passes the preflight from the apex is
 * still refused by [RequireFirstPartyOrigin] and the dedicated Origin list. Do not read this list as a description of production.
 */
export const CORS_ALLOWED_ORIGINS: readonly string[] = [
  ORIGINS.console,
  ORIGINS.accounts,
  ORIGINS.apex,
]

/** Response headers the API lets a cross-origin caller read (S21, PW6). */
export const CORS_EXPOSED_HEADERS = ["X-Password-Warning", "X-Password-Warning-Code"]
