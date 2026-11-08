import { generateKeyPairSync } from "node:crypto"

import type { Page } from "@playwright/test"

/**
 * A CDP virtual authenticator for the S20 harness specs (PK18): a platform
 * authenticator that holds discoverable credentials and verifies the person,
 * as Windows Hello or Touch ID do. Its scope is the page it is attached to, so
 * a test moves that one page between the origins.
 */
export const PASSKEY_RP_ID = "example.com"

/** The account's user handle, as the harness model's options carry it (base64url). */
export const USER_HANDLE = Buffer.from("harness-user-handle-0001").toString("base64url")

export interface VirtualAuthenticator {
  /** Adds a discoverable credential of the RP ID, as if registered earlier; its id, base64url. */
  addCredential(): Promise<string>
  credentialCount(): Promise<number>
}

export async function attachVirtualAuthenticator(
  page: Page,
  { isUserVerified = true }: { isUserVerified?: boolean } = {}
): Promise<VirtualAuthenticator> {
  const cdp = await page.context().newCDPSession(page)
  await cdp.send("WebAuthn.enable")
  const { authenticatorId } = await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified,
      automaticPresenceSimulation: true,
    },
  })
  return {
    async addCredential() {
      const { privateKey } = generateKeyPairSync("ec", { namedCurve: "prime256v1" })
      const id = Buffer.from(`harness-credential-${Date.now()}`)
      await cdp.send("WebAuthn.addCredential", {
        authenticatorId,
        credential: {
          credentialId: id.toString("base64"),
          isResidentCredential: true,
          rpId: PASSKEY_RP_ID,
          privateKey: privateKey.export({ format: "der", type: "pkcs8" }).toString("base64"),
          userHandle: Buffer.from(USER_HANDLE, "base64url").toString("base64"),
          signCount: 0,
        },
      })
      return id.toString("base64url")
    },
    async credentialCount() {
      const { credentials } = await cdp.send("WebAuthn.getCredentials", { authenticatorId })
      return credentials.length
    },
  }
}

/** What the browser signed: `clientDataJSON`'s type and origin. */
export function clientData(credential: unknown): { type: string; origin: string } {
  const encoded = (credential as { response?: { clientDataJSON?: string } }).response?.clientDataJSON
  return JSON.parse(Buffer.from(encoded ?? "", "base64url").toString("utf8")) as {
    type: string
    origin: string
  }
}

/**
 * The attestation format the browser sent (T-C11): the text after the CBOR key
 * "fmt" in `attestationObject`. A reading, not a parser: enough to record it.
 */
export function attestationFormat(credential: unknown): string {
  const encoded = (credential as { response?: { attestationObject?: string } }).response?.attestationObject
  const bytes = Buffer.from(encoded ?? "", "base64url")
  const key = Buffer.from([0x63, 0x66, 0x6d, 0x74]) // text(3) "fmt"
  const at = bytes.indexOf(key)
  if (at < 0) return "unknown"
  const header = bytes[at + key.length]
  if ((header & 0xe0) !== 0x60) return "unknown"
  const length = header & 0x1f
  return bytes.subarray(at + key.length + 1, at + key.length + 1 + length).toString("utf8")
}

/** A fresh challenge, base64url, as the server mints one. */
export function challenge(): string {
  return Buffer.from(`challenge-${Date.now()}-${Math.random()}`).toString("base64url")
}

/** The sign-in options of a deployment that runs passkeys: no credential named. */
export function assertionOptions() {
  return {
    options: {
      challenge: challenge(),
      rpId: PASSKEY_RP_ID,
      allowCredentials: [],
      userVerification: "required",
      timeout: 300_000,
    },
    expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
  }
}
