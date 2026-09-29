import { test as base, expect, type BrowserContext, type Page, type Route } from "@playwright/test"
import { existsSync, readFileSync } from "node:fs"
import { join } from "node:path"
import { fileURLToPath } from "node:url"

import { answerAnonymousDefaults, type SeenRequest } from "../isolated/accounts/mock-anonymous-api"
import { answerAuthenticatedDefaults } from "../isolated/mock-authenticated-api"
import { ApiHost } from "./api-host"
import { AttackerPages } from "./attacker-origins"
import { CspObserver } from "./csp-observer"
import { KNOWN_CSP_VIOLATIONS } from "./expected-csp-violations"
import { PrivacyMount } from "./privacy-mount"
import { startHarnessProxy, type EgressAttempt, type HarnessProxy } from "./proxy"
import { RequestLog } from "./request-log"
import { startHarnessServer } from "./server"
import { assertServableTree, createStaticHost } from "./static-host"
import { createHarnessCertificate } from "./tls"
import { HOSTS, ORIGINS, SEALED_API_ORIGIN, TOPOLOGY_HOSTS } from "./topology"
import { headerValue, parseWebConfig, type WebConfigModel } from "./web-config-model"

export { expect }
export { HOSTS, ORIGINS } from "./topology"

const AUTH_UI = fileURLToPath(new URL("../..", import.meta.url))
export const HARNESS_OUT_DIR = "dist-harness"

export type AppName = "console" | "accounts"

export interface Harness {
  /** The model each SPA host serves, read from its dist-harness/web.config. */
  models: Record<AppName, WebConfigModel>
  roots: Record<AppName, string>
  apiHost: ApiHost
  attacker: AttackerPages
  privacy: PrivacyMount
  log: RequestLog
  proxy: HarnessProxy
  /** base64 SHA-256 of the run's certificate key, for --ignore-certificate-errors-spki-list. */
  spkiSha256: string
}

/** Loads one built app and refuses to start on anything the harness cannot vouch for. */
function loadApp(app: AppName) {
  const root = join(AUTH_UI, "apps", app, HARNESS_OUT_DIR)
  if (!existsSync(join(root, "index.html"))) {
    throw new Error(
      `${HARNESS_OUT_DIR}/index.html is missing for ${app} (${root}). ` +
        "Run pnpm e2e:harness, which builds first."
    )
  }
  const file = `apps/${app}/${HARNESS_OUT_DIR}/web.config`
  const model = parseWebConfig(readFileSync(join(root, "web.config"), "utf8"), file)
  const csp = headerValue(model, "Content-Security-Policy") ?? ""
  for (const directive of ["connect-src", "img-src"]) {
    const value = new RegExp(`(?:^|;)\\s*${directive}\\s+([^;]*)`).exec(csp)?.[1] ?? ""
    if (!value.split(/\s+/).includes(SEALED_API_ORIGIN)) {
      throw new Error(
        `${file} does not name ${SEALED_API_ORIGIN} in ${directive} (found "${value}"). ` +
          "The harness topology serves the API at that origin, so the build must bake it in. " +
          "Likely cause: the VITE_ keys were not pinned to .env.production - build with " +
          "pnpm e2e:harness (scripts/build-harness.mjs), never with a plain vite build."
      )
    }
  }
  assertServableTree(root)
  return { root, model }
}

const INTERCEPTION = ["route", "routeFromHAR"] as const

/**
 * Harness pages never use route interception. Its mere presence makes Playwright
 * answer every CORS preflight itself and add permissive CORS headers to every
 * fulfilled response (playwright-core coreBundle.js 21808-21810, 35792-35809,
 * 12975-12990) - exactly the behaviour the harness exists to keep out of the way.
 */
function forbidInterception(target: Page | BrowserContext, owner: "page" | "context") {
  for (const name of INTERCEPTION) {
    Object.defineProperty(target, name, {
      configurable: true,
      value: () => {
        throw new Error(
          `${owner}.${name}() was called in a harness test: harness pages must not use ` +
            "route interception; use the api fixture (api.useAuthenticated / api.useAnonymous)."
        )
      },
    })
  }
}

export interface HarnessApi {
  /**
   * The signed-in defaults of e2e/isolated/mock-authenticated-api.ts, then
   * `handle` - the same signature installAuthenticatedApi takes. Seeds
   * auth.refreshToken on the two application origins, as that helper does.
   */
  useAuthenticated(
    permissions: string[],
    handle?: (route: Route, url: URL) => Promise<boolean>,
    options?: { preferredLanguage?: string }
  ): Promise<void>
  /** The anonymous defaults of mock-anonymous-api.ts, then `handle`. */
  useAnonymous(
    handle?: (route: Route, url: URL, body: unknown) => Promise<boolean>,
    options?: { seen?: SeenRequest[] }
  ): Promise<void>
}

export interface HarnessEgress {
  list(): readonly EgressAttempt[]
  /** Throws naming every host the browser tried to leave the topology for. */
  verify(): void
  clear(): void
}

interface TestFixtures {
  api: HarnessApi
  csp: CspObserver
  attacker: AttackerPages
  requests: RequestLog
  privacy: PrivacyMount
  egress: HarnessEgress
}

interface WorkerFixtures {
  harness: Harness
}

export const test = base.extend<TestFixtures, WorkerFixtures>({
  harness: [
    // eslint-disable-next-line no-empty-pattern -- Playwright requires the destructured first argument.
    async ({}, provide) => {
      const consoleApp = loadApp("console")
      const accountsApp = loadApp("accounts")
      const certificate = createHarnessCertificate(TOPOLOGY_HOSTS)
      const log = new RequestLog()
      const apiHost = new ApiHost()
      const attacker = new AttackerPages()
      const privacy = new PrivacyMount()
      const server = await startHarnessServer(
        certificate,
        {
          [HOSTS.console]: createStaticHost({
            name: HOSTS.console,
            root: consoleApp.root,
            model: consoleApp.model,
          }),
          [HOSTS.accounts]: createStaticHost({
            name: HOSTS.accounts,
            root: accountsApp.root,
            model: accountsApp.model,
            privacy,
          }),
          [HOSTS.api]: (request, response, url, body) => apiHost.serve(request, response, url, body),
          [HOSTS.apex]: (_, response, url) => attacker.answer(HOSTS.apex, url, response),
          [HOSTS.attacker]: (_, response, url) => attacker.answer(HOSTS.attacker, url, response),
        },
        log
      )
      const proxy = await startHarnessProxy(TOPOLOGY_HOSTS, server.port)
      try {
        await provide({
          models: { console: consoleApp.model, accounts: accountsApp.model },
          roots: { console: consoleApp.root, accounts: accountsApp.root },
          apiHost,
          attacker,
          privacy,
          log,
          proxy,
          spkiSha256: certificate.spkiSha256,
        })
      } finally {
        await proxy.close()
        await server.close()
        certificate.dispose()
      }
    },
    { scope: "worker", timeout: 60_000 },
  ],

  context: async ({ browser, harness, viewport }, provide) => {
    const context = await browser.newContext({
      viewport,
      proxy: { server: harness.proxy.url },
      ignoreHTTPSErrors: true,
      serviceWorkers: "block",
    })
    forbidInterception(context, "context")
    for (const page of context.pages()) forbidInterception(page, "page")
    context.on("page", (page) => forbidInterception(page, "page"))
    await provide(context)
    await context.close()
  },

  requests: async ({ harness }, provide) => {
    harness.log.clear()
    await provide(harness.log)
  },

  attacker: async ({ harness }, provide) => {
    harness.attacker.clear()
    await provide(harness.attacker)
    harness.attacker.clear()
  },

  privacy: async ({ harness }, provide) => {
    harness.privacy.clear()
    await provide(harness.privacy)
    harness.privacy.clear()
  },

  api: async ({ harness, context }, provide) => {
    harness.apiHost.clear()
    const seedRefreshToken = async () => {
      await context.addInitScript(
        (origins: string[]) => {
          if (!origins.includes(location.origin)) return
          localStorage.setItem("auth.refreshToken", "isolated-refresh")
        },
        [ORIGINS.console, ORIGINS.accounts]
      )
    }
    await provide({
      async useAuthenticated(permissions, handle = async () => false, options) {
        await seedRefreshToken()
        harness.apiHost.answerWith(
          async (route, url) =>
            (await answerAuthenticatedDefaults(route.asRoute(), url, permissions, options)) ||
            (await handle(route.asRoute(), url))
        )
      },
      async useAnonymous(handle = async () => false, options) {
        harness.apiHost.answerWith(async (route, url) => {
          const request = route.request()
          const path = url.pathname.toLowerCase()
          const raw = request.postData()
          const body = raw ? (JSON.parse(raw) as unknown) : undefined
          if (request.method() !== "GET") {
            options?.seen?.push({ method: request.method(), path, body })
          }
          return (
            (await answerAnonymousDefaults(route.asRoute(), path)) ||
            (await handle(route.asRoute(), url, body))
          )
        })
      },
    })
    harness.apiHost.clear()
  },

  csp: [
    async ({ context }, provide) => {
      const observer = await CspObserver.attach(context)
      for (const known of KNOWN_CSP_VIOLATIONS) observer.tolerate(known)
      await provide(observer)
      await observer.settle()
      observer.verify()
    },
    { auto: true },
  ],

  egress: [
    async ({ harness, context }, provide) => {
      harness.proxy.clearEgress()
      // The proxy sees only host:port; the context says which page asked.
      const pagesByHost = new Map<string, Set<string>>()
      context.on("request", (request) => {
        let host: string
        try {
          host = new URL(request.url()).host
        } catch {
          return
        }
        let pageUrl = "(no page)"
        try {
          pageUrl = request.frame().page().url()
        } catch {
          // Service-worker and some navigation requests have no frame.
        }
        const set = pagesByHost.get(host) ?? new Set<string>()
        set.add(pageUrl)
        pagesByHost.set(host, set)
      })
      const egress: HarnessEgress = {
        list: () => harness.proxy.egress(),
        clear: () => harness.proxy.clearEgress(),
        verify: () => {
          const attempts = harness.proxy.egress()
          if (!attempts.length) return
          const lines = attempts.map((attempt) => {
            const host = attempt.kind === "CONNECT" ? attempt.target.replace(/:443$/, "") : new URL(attempt.target).host
            const pages = [...(pagesByHost.get(host) ?? [])]
            return `  ${attempt.kind} ${attempt.target} requested by ${pages.length ? pages.join(", ") : "(unknown page)"}`
          })
          throw new Error(
            "The browser tried to leave the harness topology; the proxy refused and nothing " +
              `reached the network:\n${lines.join("\n")}\n` +
              "Serve that host from the test (add it to e2e/harness/topology.ts) or remove the request."
          )
        },
      }
      await provide(egress)
      egress.verify()
    },
    { auto: true },
  ],
})
