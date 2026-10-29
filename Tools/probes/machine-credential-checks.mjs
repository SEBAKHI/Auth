/**
 * Live proof of the API-key check of OI-128 PR B (T11) against a running dev API: an application's
 * server, signed in with its own machine key, validates one of its own API keys (200, its own code)
 * and hears exactly the answer of a key that exists nowhere for another application's key. The
 * endpoint tests pin each rule with a mocked repository; this shows the deployed pipeline
 * (machine token, routing, the selector lookup, the binding) doing the same on the real database.
 *
 * Run it only against a development API, never against a shared test deployment or production: it
 * writes rows (one ClientAssertionJtis row for the machine token, LastUsedAt on the own key).
 *
 * Usage:
 *   node Tools/probes/machine-credential-checks.mjs --pem <private-key.pem> --kid <kid> --client-id <code>
 *        --own-key <file> --other-key <file>
 *
 * Prepare, in the console, two applications A and B:
 *   - A's page, tab "Application keys" («مفاتيح التطبيق»): "Generate key" («إنشاء مفتاح»), purpose
 *     machine, download the private key (--pem, --kid,
 *     and A's code as --client-id).
 *   - An API key of A and an API key of B («مفاتيح API», "Create API key"), each saved to its own
 *     text file (--own-key, --other-key). Files, not arguments: a key on the command line lands in
 *     the shell history.
 * Revoke the machine key and both API keys afterwards.
 *
 * Environment:
 *   PROBE_API_URL         default https://localhost:5101 (must be localhost: the probe refuses
 *                         any other host, and accepts the dev certificate only there)
 *   PROBE_GATEWAY_TOKEN   optional X-Gateway-Token, when the dev API validates it
 *
 * Checks:
 *   no-token    the check without a bearer token: 401.
 *   own         A's key with A's machine token: 200, no-store, active, applicationCode = A's code.
 *   unknown     a well-formed key whose selector exists nowhere: 400 ApiKey.Invalid.
 *   other       B's key with A's machine token: 400 ApiKey.Invalid, with the same headers and the
 *               same body as unknown (traceId aside): A cannot tell B's key from no key.
 * Exit code 0 only when every check passed.
 */
import { createPrivateKey, randomBytes, randomInt, sign } from "node:crypto"
import { readFileSync } from "node:fs"
import https from "node:https"
import http from "node:http"
import { crc32 } from "node:zlib"

function argument(name) {
  const index = process.argv.indexOf(name)
  return index > 0 ? process.argv[index + 1] : undefined
}

const pemPath = argument("--pem")
const kid = argument("--kid")
const clientId = argument("--client-id")
const ownKeyPath = argument("--own-key")
const otherKeyPath = argument("--other-key")
if (!pemPath || !kid || !clientId || !ownKeyPath || !otherKeyPath) {
  console.error("Usage: node Tools/probes/machine-credential-checks.mjs --pem <file> --kid <kid> --client-id <code> --own-key <file> --other-key <file>")
  process.exit(2)
}

const api = new URL(process.env.PROBE_API_URL ?? "https://localhost:5101")
if (!["localhost", "127.0.0.1", "[::1]"].includes(api.hostname)) {
  console.error(`Refusing ${api.hostname}: this probe runs against a local development API only.`)
  process.exit(2)
}

const privateKey = createPrivateKey(readFileSync(pemPath, "utf8"))
if (privateKey.asymmetricKeyType !== "ec" || privateKey.asymmetricKeyDetails?.namedCurve !== "prime256v1") {
  console.error("The private key must be an EC P-256 key.")
  process.exit(2)
}

const b64url = (value) => Buffer.from(value).toString("base64url")

function assertion(audience) {
  const now = Math.floor(Date.now() / 1000)
  const header = { alg: "ES256", kid, typ: "client-authentication+jwt" }
  const payload = { iss: clientId, sub: clientId, aud: audience, jti: randomBytes(16).toString("hex"), iat: now, exp: now + 60 }
  const input = `${b64url(JSON.stringify(header))}.${b64url(JSON.stringify(payload))}`
  // JWS ES256 is the raw r||s pair (IEEE P1363), not DER.
  const signature = sign("sha256", Buffer.from(input), { key: privateKey, dsaEncoding: "ieee-p1363" })
  return `${input}.${b64url(signature)}`
}

// ApiKeyFormat: ak_<env>_<selector16>_<secret32><check6>, the check being the CRC32 of everything
// before it, written as six base62 digits.
const BASE62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
const base62 = (length) => Array.from({ length }, () => BASE62[randomInt(62)]).join("")

function check6(body) {
  let value = crc32(Buffer.from(body, "ascii"))
  const digits = []
  for (let i = 0; i < 6; i++) {
    digits.unshift(BASE62[value % 62])
    value = Math.floor(value / 62)
  }
  return digits.join("")
}

const wellFormedUnknownKey = () => {
  const body = `ak_dev_${base62(16)}_${base62(32)}`
  return body + check6(body)
}

// The server's shape and check: a key minted before this format, or a typo, would get
// ApiKey.Invalid for its shape alone, and "other" would then pass without proving the binding.
const isWellFormed = (raw) =>
  /^ak_(prod|stag|dev|othr)_[0-9A-Za-z]{16}_[0-9A-Za-z]{38}$/.test(raw) && raw.slice(-6) === check6(raw.slice(0, -6))

const ownKey = readFileSync(ownKeyPath, "utf8").trim()
const otherKey = readFileSync(otherKeyPath, "utf8").trim()
for (const [name, key] of [["--own-key", ownKey], ["--other-key", otherKey]]) {
  if (!isWellFormed(key)) {
    console.error(`${name} does not hold an API key of the current format (ak_<env>_<selector>_<secret><check>): create a new key.`)
    process.exit(2)
  }
}
if (ownKey === otherKey) {
  console.error("--own-key and --other-key hold the same key.")
  process.exit(2)
}

function request(method, path, { form, json, bearer } = {}) {
  const url = new URL(path, api)
  const transport = url.protocol === "https:" ? https : http
  const headers = { Accept: "application/json" }
  let body
  if (form) {
    headers["Content-Type"] = "application/x-www-form-urlencoded"
    body = form
  } else if (json) {
    headers["Content-Type"] = "application/json"
    body = JSON.stringify(json)
  }
  if (bearer) headers.Authorization = `Bearer ${bearer}`
  if (process.env.PROBE_GATEWAY_TOKEN) headers["X-Gateway-Token"] = process.env.PROBE_GATEWAY_TOKEN

  return new Promise((resolve, reject) => {
    const req = transport.request(url, {
      method,
      headers,
      // The dev certificate, on localhost only (checked above).
      rejectUnauthorized: false,
    }, (res) => {
      let text = ""
      res.setEncoding("utf8")
      res.on("data", (chunk) => (text += chunk))
      res.on("end", () => {
        let parsed
        try { parsed = JSON.parse(text) } catch { parsed = undefined }
        resolve({ status: res.statusCode, headers: res.headers, json: parsed })
      })
    })
    req.on("error", reject)
    if (body) req.write(body)
    req.end()
  })
}

const discovery = await request("GET", "/.well-known/openid-configuration")
if (discovery.status !== 200) {
  console.error(`Discovery answered ${discovery.status}; is the API running at ${api}?`)
  process.exit(2)
}

const token = await request("POST", new URL(discovery.json.token_endpoint).pathname, {
  form: new URLSearchParams({
    grant_type: "client_credentials",
    client_assertion_type: "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
    client_assertion: assertion(discovery.json.issuer),
    client_id: clientId,
  }).toString(),
})
if (token.status !== 200 || !token.json?.access_token) {
  console.error(`The token endpoint answered ${token.status}${token.json?.code ? `, ${token.json.code}` : ""}; check --pem, --kid and --client-id.`)
  process.exit(2)
}
const machineToken = token.json.access_token

const validatePath = "/api/v1/app/machine/apikeys/validate"
const validate = (apiKey, bearer) => request("POST", validatePath, { json: { apiKey }, bearer })

const results = []
function check(name, pass, detail) {
  results.push({ name, pass })
  console.log(`${pass ? "PASS" : "FAIL"}  ${name}${detail ? `  (${detail})` : ""}`)
}

const noToken = await validate(ownKey, undefined)
check("no-token", noToken.status === 401, `status ${noToken.status}`)

const own = await validate(ownKey, machineToken)
check("own",
  own.status === 200
    && /no-store/.test(own.headers["cache-control"] ?? "")
    && own.json?.active === true
    && own.json?.applicationCode?.toUpperCase() === clientId.toUpperCase(),
  `status ${own.status}${own.json?.code ? `, ${own.json.code}` : ""}`)

const unknown = await validate(wellFormedUnknownKey(), machineToken)
check("unknown", unknown.status === 400 && unknown.json?.code === "ApiKey.Invalid", `status ${unknown.status}, ${unknown.json?.code}`)

const other = await validate(otherKey, machineToken)
{
  const withoutTrace = (json) => JSON.stringify({ ...json, traceId: undefined })
  // Headers that differ on every answer whatever the key: the clock and the per-request ids.
  const varying = new Set(["date", "x-correlation-id", "x-request-id", "traceparent"])
  const shape = (headers) => Object.keys(headers).filter((h) => !varying.has(h)).sort()
    .map((h) => `${h}: ${h === "content-length" ? "" : headers[h]}`).join("\n")
  const same = withoutTrace(other.json) === withoutTrace(unknown.json) && shape(other.headers) === shape(unknown.headers)
  check("other",
    other.status === 400 && other.json?.code === "ApiKey.Invalid" && same,
    `status ${other.status}, ${other.json?.code}${same ? ", same as unknown" : ", DIFFERS from unknown"}`)
}

console.log(`\nRevoke the probe's machine key (${kid}) and both API keys in the console when you are done.`)
process.exit(results.every((r) => r.pass) ? 0 : 1)
