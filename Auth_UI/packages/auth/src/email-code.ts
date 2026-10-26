import { api } from "@authsystem/api/client"
import { getErrorCodes } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"

/**
 * The two requests of a sign-in by emailed code that come before the account
 * decides anything (OI-215): the address, then the code.
 *
 * Every address gets the same answer to the first one — a handle, the address
 * masked, an expiry — and one message, chosen by the server and seen only by the
 * mailbox's holder: a sign-in code, a registration code, or a notice. Nothing in
 * the page may branch on the account before the code matched; after it, the
 * server says what comes next.
 */

/** A code the server has sent (or, for an address it will not mail, has not). */
export interface PendingEmailCode {
  /** The opaque handle the server issued; it names the row, it opens nothing. */
  pendingId: string
  /** The address as typed: shown back, and used to ask for a new code. */
  email: string
  /** The address as the server shows it back. */
  maskedEmail: string
  /** ISO instant the current code stops being accepted. */
  expiresAt: string
}

export type EmailCodeStep =
  | "signIn"
  | "recover"
  | "completeAccount"
  | "passwordRequired"

export interface EmailCodeNextStep {
  step: EmailCodeStep
  /** For "completeAccount": whether the new account must set a password. */
  passwordRequired: boolean
  /** For "completeAccount": whether it must give a first and last name. */
  nameRequired: boolean
}

const STEPS: readonly string[] = [
  "signIn",
  "recover",
  "completeAccount",
  "passwordRequired",
]

/**
 * The server's next step as this build knows it, or null for a step it does
 * not: a newer server's step is never guessed at. Required fields default to
 * required, the direction that can only ask for more.
 */
export function toEmailCodeNextStep(
  raw:
    | {
        step?: string | null
        passwordRequired?: boolean | null
        nameRequired?: boolean | null
      }
    | null
    | undefined
): EmailCodeNextStep | null {
  if (!raw?.step || !STEPS.includes(raw.step)) return null
  return {
    step: raw.step as EmailCodeStep,
    passwordRequired: raw.passwordRequired !== false,
    nameRequired: raw.nameRequired !== false,
  }
}

export async function startEmailCode(
  email: string,
  preferredLanguage: string
): Promise<PendingEmailCode> {
  const data = await unwrap(
    api.POST("/api/v1/Auth/email-code/start", {
      body: { email, preferredLanguage },
    })
  )
  return {
    pendingId: data.pendingId,
    email,
    maskedEmail: data.maskedEmail,
    expiresAt: data.expiresAt,
  }
}

/** Checks the code without spending it; answers only after it matched. */
export async function verifyEmailCode(
  pendingId: string,
  otp: string
): Promise<EmailCodeNextStep> {
  const data = await unwrap(
    api.POST("/api/v1/Auth/email-code/verify", { body: { pendingId, otp } })
  )
  const next = toEmailCodeNextStep(data)
  if (!next) throw new Error("Unknown email-code step")
  return next
}

/**
 * The sign-up's start: the sign-in page's, so an address that already has an
 * account gets a sign-in code. While email sending is off that start refuses
 * every address, and the registration's own start takes over (development
 * writes its code to the API log). Both write the same pending row, and the code
 * check that follows is the same.
 */
export async function startSignUpCode(
  email: string,
  preferredLanguage: string
): Promise<PendingEmailCode> {
  try {
    return await startEmailCode(email, preferredLanguage)
  } catch (error) {
    if (!getErrorCodes(error).includes("User.EmailCodeSignInUnavailable")) {
      throw error
    }
  }
  const data = await unwrap(
    api.POST("/api/v1/Auth/registration/start", {
      body: { email, preferredLanguage },
    })
  )
  return {
    pendingId: data.pendingId,
    email,
    maskedEmail: data.maskedEmail,
    expiresAt: data.expiresAt,
  }
}
