import type { Schemas } from "@authsystem/api/types"
import { formatDateTime } from "@authsystem/ui/format"

import type { Translate } from "./keyring-capabilities"

/**
 * What the signing keyring card offers (S12), as pure functions of the keyring the
 * API returned: one table read by the buttons and by the reasons shown beside them,
 * so a disabled control always says why.
 */

export type SigningKeyring = Schemas["SigningKeyringDto"]
export type SigningKeyringEntry = Schemas["SigningKeyringEntryDto"]

/**
 * The generated schema types `SigningKeyState` as a number (the OpenAPI document
 * describes the CLR enum), but the API writes enums by name.
 */
export type SigningKeyState = "Current" | "Next" | "Previous" | "Compromised"

export function signingStateOf(entry: SigningKeyringEntry): SigningKeyState {
  return entry.state as unknown as SigningKeyState
}

/** Why a card action is unavailable, or null when it is available. */
export interface SigningKeyringCapabilities {
  stage: string | null
  promote: string | null
  discard: string | null
  emergency: string | null
}

export function signingKeyringCapabilities(
  keyring: SigningKeyring,
  t: Translate,
  now: Date = new Date()
): SigningKeyringCapabilities {
  const staged = keyring.keys.find(
    (entry) => entry.inFile && signingStateOf(entry) === "Next"
  )
  const noStaged = t("secrets.signingKeyring.reason.noStaged")

  return {
    stage: staged ? t("secrets.signingKeyring.reason.stageWhileStaged") : null,
    // A staged key loaded here but no longer accepted is past its 30 days: no
    // restart brings it back, only a discard and a new stage.
    promote: !staged
      ? noStaged
      : !staged.loadedInProcess
        ? t("secrets.signingKeyring.reason.restartToLoad")
        : !staged.acceptedInProcess
          ? t("secrets.signingKeyring.reason.stagedExpired")
          : !keyring.promoteAvailableAtUtc
            ? t("secrets.signingKeyring.reason.notRecorded")
            : new Date(keyring.promoteAvailableAtUtc).getTime() > now.getTime()
              ? t("secrets.signingKeyring.reason.tooYoung", {
                  date: formatDateTime(keyring.promoteAvailableAtUtc),
                })
              : null,
    discard: staged ? null : noStaged,
    // An emergency promotes the staged key: stage one first, after a leak a new
    // one (a key staged before the leak leaked with the file).
    emergency: staged ? null : t("secrets.signingKeyring.reason.emergencyNeedsStaged"),
  }
}

/** What a key's row offers: a retire with why it is unavailable, or a note when it offers none. */
export interface SigningKeyringRowCapabilities {
  retire: string | null
  note: string | null
}

export function signingKeyringRowCapabilities(
  entry: SigningKeyringEntry,
  t: Translate,
  processCurrentKid?: string | null
): SigningKeyringRowCapabilities {
  if (!entry.inFile) {
    return { retire: null, note: t("secrets.signingKeyring.reason.processOnly") }
  }

  const state = signingStateOf(entry)
  if (state === "Current") {
    return { retire: null, note: t("secrets.signingKeyring.reason.current") }
  }
  if (state === "Next") {
    return { retire: null, note: t("secrets.signingKeyring.reason.next") }
  }
  // Another API process promoted and this one was never recycled: it still signs
  // with this key, which the API refuses to retire until this process restarts.
  if (entry.kid === processCurrentKid) {
    return { retire: null, note: t("secrets.signingKeyring.reason.processSigns") }
  }
  return { retire: null, note: null }
}

/**
 * Compromised keys this process still accepts: an emergency saved in the file that
 * this process applies only at its next start. Shown as an alert, never left for the
 * administrator to infer from a table cell.
 */
export function compromisedStillAccepted(keyring: SigningKeyring): string[] {
  return keyring.keys
    .filter((entry) => entry.compromised && entry.acceptedInProcess)
    .map((entry) => entry.kid)
}
