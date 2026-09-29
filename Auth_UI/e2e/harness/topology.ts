/**
 * The harness's five HTTPS hosts. All but the last share one registrable
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
  /** Cross-site attacker. */
  attacker: "attacker.example.net",
} as const

export type HostName = (typeof HOSTS)[keyof typeof HOSTS]

export const ORIGINS = {
  console: `https://${HOSTS.console}`,
  accounts: `https://${HOSTS.accounts}`,
  api: `https://${HOSTS.api}`,
  apex: `https://${HOSTS.apex}`,
  attacker: `https://${HOSTS.attacker}`,
} as const

export const TOPOLOGY_HOSTS: readonly string[] = Object.values(HOSTS)

/** The API origin the sealed dist-harness/web.config must name (target state 3). */
export const SEALED_API_ORIGIN = ORIGINS.api

/**
 * Origins the API host answers CORS for, with credentials.
 *
 * DELIBERATELY STRICTER TEST THAN PRODUCTION (STAGE-3 ARR-1369/1370): the
 * production CORS list no longer contains the apex (owner decision 2026-09-28).
 * It stays here so a harness test can show that a request which passes the
 * preflight from the apex is still refused by [RequireFirstPartyOrigin] and the
 * dedicated Origin list. Do not read this list as a description of production.
 */
export const CORS_ALLOWED_ORIGINS: readonly string[] = [
  ORIGINS.console,
  ORIGINS.accounts,
  ORIGINS.apex,
]

/** Response headers the API lets a cross-origin caller read (S21, PW6). */
export const CORS_EXPOSED_HEADERS = ["X-Password-Warning", "X-Password-Warning-Code"]
