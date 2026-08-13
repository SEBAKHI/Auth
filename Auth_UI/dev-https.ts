import fs from "node:fs"
import type { ServerOptions as HttpsServerOptions } from "node:https"

import { loadEnv, type ConfigEnv } from "vite"

/**
 * Dev-server TLS, shared by both apps.
 *
 * The Auth API serves https in development, and Chrome's schemeful same-site
 * counts http://localhost and https://localhost as DIFFERENT sites. The IdP
 * session cookie is `SameSite=Lax` and is minted on the response to a *fetch*
 * (`POST /auth/login`), which is a cross-site subresource once the schemes
 * differ — so Chrome silently refuses to STORE it. Login succeeds, the browser
 * keeps nothing to prove it, and /auth/authorize bounces straight back to
 * /login: an endless loop with no error anywhere. Serving the SPAs over https
 * too makes dev match production (accounts-sandbox.sebakhi.com + auth-sandbox.sebakhi.com are
 * both https on one registrable domain, i.e. same-site).
 *
 * Note `Secure` is not what breaks: Chrome treats http://localhost as a
 * trustworthy origin and accepts Secure cookies on it. The scheme is.
 */

/** Env keys holding absolute paths to a locally trusted PEM cert and its key. */
const CERT_VAR = "DEV_HTTPS_CERT"
const KEY_VAR = "DEV_HTTPS_KEY"

/**
 * Resolves the dev server's TLS options from `.env.development.local`.
 *
 * Returns undefined — after warning loudly — when the vars are unset, so a
 * fresh clone still runs `pnpm dev`. Silence would be worse than the fallback:
 * the developer would hit the login loop above with nothing to point at. The
 * warning is scoped to `serve` because `vite build` evaluates this config too,
 * and TLS says nothing about a production bundle.
 *
 * The paths are read here rather than handed to Vite as strings because Vite's
 * own reader swallows a missing file (`readFileIfExists` catches and passes the
 * path through as if it were the certificate), which surfaces as an opaque
 * error from Node instead of the wrong path.
 */
export function devHttps(
  { command, mode }: ConfigEnv,
  configDir: string
): HttpsServerOptions | undefined {
  if (command !== "serve") return undefined

  // Explicit prefixes: `""` would pull all of process.env into the result.
  const env = loadEnv(mode, configDir, ["VITE_", "DEV_HTTPS_"])
  const certPath = env[CERT_VAR]
  const keyPath = env[KEY_VAR]

  if (!certPath || !keyPath) {
    console.warn(
      `\n  [dev-https] ${CERT_VAR}/${KEY_VAR} not set - serving http.\n` +
        `  OAuth sign-in will loop between /login and /auth/authorize: Chrome\n` +
        `  drops the IdP session cookie when the SPA and the API differ in scheme.\n` +
        `  Fix (once per machine):\n` +
        `    dotnet dev-certs https --export-path "$env:USERPROFILE\\.aspnet\\https\\localhost.pem" --format PEM --no-password\n` +
        `  then set ${CERT_VAR}/${KEY_VAR} to the .pem/.key paths in .env.development.local.\n`
    )
    return undefined
  }

  return { cert: fs.readFileSync(certPath), key: fs.readFileSync(keyPath) }
}
