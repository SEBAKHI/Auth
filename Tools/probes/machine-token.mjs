/**
 * Live proof of the client_credentials grant (OI-128, T10) against a running dev API: a machine key
 * signs an assertion, the token endpoint answers a machine token once, and refuses the same
 * assertion the second time. The unit tests pin each rule with real keys; this shows the deployed
 * pipeline (routing, form binding, the database's single-use statement) doing the same.
 *
 * Run it only against a development API, never against a shared test deployment or production: it writes rows
 * (one ClientAssertionJtis row per accepted assertion, LastUsedAt on the key).
 *
 * Usage:
 *   node Tools/probes/machine-token.mjs --pem <private-key.pem> --kid <kid> --client-id <application code> [--burst]
 *
 * Get the PEM and the kid from the console: the application's page, tab "Application keys"
 * («مفاتيح التطبيق»), "Generate key" («إنشاء مفتاح»), purpose machine, then download the private
 * key. Revoke the key afterwards.
 *
 * Environment:
 *   PROBE_API_URL         default https://localhost:5101 (must be localhost: the probe refuses
 *                         any other host, and accepts the dev certificate only there)
 *   PROBE_GATEWAY_TOKEN   optional X-Gateway-Token, when the dev API validates it
 *
 * Checks:
 *   first       a fresh assertion: 200, expires_in 300, no refresh_token, and a token whose header
 *               typ is at+jwt and whose claims are exactly iss, sub, client_id, aud, scope, jti,
 *               iat, exp, with aud urn:authsystem:machine and sub = client_id = the client id.
 *   replay      the same assertion again: 400 Auth.InvalidClient.
 *   endpoint-aud  an assertion addressed to the token endpoint instead of the issuer:
 *               400 Auth.InvalidClient.
 *   burst       (with --burst) one fresh assertion sent ten times at once: exactly one 200, nine
 *               Auth.InvalidClient. Plain parallel requests may still reach the database one at a
 *               time, so a PASS here shows the answer is right, not that the requests overlapped;
 *               the statement's atomicity is pinned by MachineCredentialSqlTests.
 * Exit code 0 only when every check that ran passed.
 */
import { createPrivateKey, randomBytes, sign } from "node:crypto"
import { readFileSync } from "node:fs"
import https from "node:https"
import http from "node:http"

function argument(name) {
  const index = process.argv.indexOf(name)
  return index > 0 ? process.argv[index + 1] : undefined
}

const pemPath = argument("--pem")
const kid = argument("--kid")
const clientId = argument("--client-id")
const burst = process.argv.includes("--burst")
if (!pemPath || !kid || !clientId) {
  console.error("Usage: node Tools/probes/machine-token.mjs --pem <file> --kid <kid> --client-id <code> [--burst]")
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

function request(method, path, body) {
  const url = new URL(path, api)
  const transport = url.protocol === "https:" ? https : http
  const headers = { Accept: "application/json" }
  if (body) headers["Content-Type"] = "application/x-www-form-urlencoded"
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
        let json
        try { json = JSON.parse(text) } catch { json = undefined }
        resolve({ status: res.statusCode, json })
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
const issuer = discovery.json.issuer
const tokenEndpoint = discovery.json.token_endpoint
const tokenPath = new URL(tokenEndpoint).pathname

const form = (clientAssertion) => new URLSearchParams({
  grant_type: "client_credentials",
  client_assertion_type: "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
  client_assertion: clientAssertion,
  client_id: clientId,
}).toString()

const results = []
function check(name, pass, detail) {
  results.push({ name, pass })
  console.log(`${pass ? "PASS" : "FAIL"}  ${name}${detail ? `  (${detail})` : ""}`)
}

const decode = (part) => JSON.parse(Buffer.from(part, "base64url").toString("utf8"))

// first + replay
const fresh = assertion(issuer)
const first = await request("POST", tokenPath, form(fresh))
{
  const ok = first.status === 200 && first.json?.expires_in === 300 && !("refresh_token" in (first.json ?? {}))
  let shape = false
  if (ok) {
    const [h, p] = first.json.access_token.split(".")
    const header = decode(h)
    const claims = decode(p)
    const names = Object.keys(claims).sort().join(",")
    shape = header.typ === "at+jwt"
      && names === ["aud", "client_id", "exp", "iat", "iss", "jti", "scope", "sub"].join(",")
      && claims.aud === "urn:authsystem:machine"
      && claims.sub === clientId && claims.client_id === clientId
      && claims.exp - claims.iat === 300
  }
  check("first", ok && shape, `status ${first.status}${first.json?.code ? `, ${first.json.code}` : ""}`)
}

const replay = await request("POST", tokenPath, form(fresh))
check("replay", replay.status === 400 && replay.json?.code === "Auth.InvalidClient", `status ${replay.status}, ${replay.json?.code}`)

const wrongAudience = await request("POST", tokenPath, form(assertion(tokenEndpoint)))
check("endpoint-aud", wrongAudience.status === 400 && wrongAudience.json?.code === "Auth.InvalidClient",
  `status ${wrongAudience.status}, ${wrongAudience.json?.code}`)

if (burst) {
  const shared = assertion(issuer)
  const answers = await Promise.all(Array.from({ length: 10 }, () => request("POST", tokenPath, form(shared))))
  const ok = answers.filter((a) => a.status === 200).length
  const refused = answers.filter((a) => a.status === 400 && a.json?.code === "Auth.InvalidClient").length
  const limited = answers.filter((a) => a.status === 429).length
  if (limited > 0) {
    console.log(`INCONCLUSIVE  burst  (${limited} answers were 429: wait a minute for the login bucket and run again)`)
  } else {
    check("burst", ok === 1 && refused === 9, `${ok} tokens, ${refused} refused`)
  }
}

console.log(`\nRevoke the probe's key in the console when you are done (key ${kid}).`)
process.exit(results.every((r) => r.pass) ? 0 : 1)
