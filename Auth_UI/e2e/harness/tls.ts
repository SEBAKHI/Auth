import { execFileSync } from "node:child_process"
import { X509Certificate, createHash } from "node:crypto"
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"

export interface HarnessCertificate {
  cert: Buffer
  key: Buffer
  /**
   * base64 SHA-256 of the SubjectPublicKeyInfo, for the named fallback of spike
   * item 2 (--ignore-certificate-errors-spki-list). Unused while
   * ignoreHTTPSErrors works; kept for S20, whose WebAuthn check may need it.
   */
  spkiSha256: string
  /** How it was made, for the run log. */
  source: string
}

/**
 * Where openssl is found without asking anyone to install it: PATH first, then
 * the two copies Git for Windows ships (the owner's machine has the first; a
 * windows-latest runner is expected to have Git too, which S30b confirms).
 */
const OPENSSL_CANDIDATES = [
  "openssl",
  "C:/Program Files/Git/usr/bin/openssl.exe",
  "C:/Program Files/Git/mingw64/bin/openssl.exe",
]

function run(command: string, args: string[]) {
  return execFileSync(command, args, { stdio: "pipe", windowsHide: true })
}

function withOpenssl(dir: string, hosts: readonly string[], tried: string[]) {
  for (const candidate of OPENSSL_CANDIDATES) {
    try {
      run(candidate, ["version"])
    } catch {
      tried.push(`${candidate} version`)
      continue
    }
    // A config file rather than -subj/-addext: MSYS builds of openssl rewrite
    // arguments that look like POSIX paths ("/CN=..."), a config file is read as is.
    const config = join(dir, "harness.cnf")
    writeFileSync(
      config,
      [
        "[req]",
        "prompt = no",
        "distinguished_name = dn",
        "x509_extensions = ext",
        "[dn]",
        "CN = harness.invalid",
        "[ext]",
        `subjectAltName = ${hosts.map((host) => `DNS:${host}`).join(",")}`,
        "basicConstraints = critical,CA:FALSE",
        "keyUsage = critical,digitalSignature",
        "extendedKeyUsage = serverAuth",
        "",
      ].join("\n")
    )
    const args = [
      "req",
      "-x509",
      "-newkey",
      "ec",
      "-pkeyopt",
      "ec_paramgen_curve:prime256v1",
      "-nodes",
      "-days",
      "2",
      "-keyout",
      join(dir, "key.pem"),
      "-out",
      join(dir, "cert.pem"),
      "-config",
      config,
    ]
    try {
      run(candidate, args)
      return `openssl (${candidate})`
    } catch (error) {
      tried.push(`${candidate} ${args.join(" ")}: ${String(error).split("\n")[0]}`)
    }
  }
  return undefined
}

/**
 * The named fallback (card S30a, spike item 5): the ASP.NET Core development
 * certificate, exported as PEM. Its name is "localhost", which the certificate
 * override accepts. `--check` runs first so the harness never CREATES a
 * certificate in the user's store (H7): no dev cert, no fallback.
 */
function withDotnetDevCert(dir: string, tried: string[]) {
  try {
    run("dotnet", ["dev-certs", "https", "--check"])
  } catch {
    tried.push("dotnet dev-certs https --check (no development certificate, or no dotnet)")
    return undefined
  }
  const certPath = join(dir, "cert.pem")
  const args = ["dev-certs", "https", "--export-path", certPath, "--format", "PEM", "--no-password"]
  try {
    run("dotnet", args)
  } catch (error) {
    tried.push(`dotnet ${args.join(" ")}: ${String(error).split("\n")[0]}`)
    return undefined
  }
  // dotnet writes the key beside the certificate as <name>.key.
  const keyPath = join(dir, "cert.key")
  if (!existsSync(keyPath)) {
    tried.push(`dotnet ${args.join(" ")}: no ${keyPath} written`)
    return undefined
  }
  writeFileSync(join(dir, "key.pem"), readFileSync(keyPath))
  return "dotnet dev-certs (localhost)"
}

/** A fresh self-signed certificate naming every topology host, for one worker. */
export function createHarnessCertificate(hosts: readonly string[]): HarnessCertificate {
  const dir = mkdtempSync(join(tmpdir(), "authsystem-harness-tls-"))
  const tried: string[] = []
  // The folder goes as soon as the pair is in memory: a killed worker must not
  // leave a private key behind (with the dotnet fallback, an unencrypted export
  // of the user's development certificate key).
  try {
    const source = withOpenssl(dir, hosts, tried) ?? withDotnetDevCert(dir, tried)
    if (!source) {
      throw new Error(
        "The browser harness needs a throwaway TLS certificate and could not make one.\n" +
          "It uses openssl (on PATH or from Git for Windows), and falls back to an existing\n" +
          "ASP.NET Core development certificate (dotnet dev-certs https). Tried:\n  " +
          tried.join("\n  ")
      )
    }
    const cert = readFileSync(join(dir, "cert.pem"))
    const key = readFileSync(join(dir, "key.pem"))
    const spkiSha256 = createHash("sha256")
      .update(new X509Certificate(cert).publicKey.export({ type: "spki", format: "der" }))
      .digest("base64")
    return { cert, key, spkiSha256, source }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}
