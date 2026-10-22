import { toNumber } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { formatDateTime } from "@authsystem/ui/format"

/**
 * What the keyring panel offers, as pure functions of the keyring the API
 * returned: one table read by the buttons and by the reasons shown beside them.
 */

export type HmacKeyring = Schemas["HmacKeyringDto"]
export type HmacKeyringEntry = Schemas["HmacKeyringEntryDto"]

/**
 * The generated schema types `HmacKeyState` as a number (the OpenAPI document
 * describes the CLR enum), but the API writes enums by name, as everywhere else
 * on this page.
 */
export type KeyState = "Current" | "Next" | "Previous"

export function stateOf(entry: HmacKeyringEntry): KeyState {
  return entry.state as unknown as KeyState
}

/** Whether an instant the server sent is still ahead of `now`. */
function stillAhead(value: string | null | undefined, now: Date): boolean {
  return Boolean(value) && new Date(value as string).getTime() >= now.getTime()
}

/**
 * Why a panel action is unavailable, or null when it is available. One table,
 * read by the buttons and by the reasons shown under them, so a disabled
 * control always says why (a disabled button with no reason reads as broken).
 */
export interface KeyringCapabilities {
  stage: string | null
  promote: string | null
  discard: string | null
}

export type Translate = (key: string, options?: Record<string, unknown>) => string

export function keyringCapabilities(
  keyring: HmacKeyring,
  t: Translate
): KeyringCapabilities {
  const staged = keyring.keys.find((entry) => stateOf(entry) === "Next")
  const stagedId = staged ? toNumber(staged.id) : undefined

  return {
    stage: staged
      ? t("secrets.keyring.reason.stageWhileStaged", { id: stagedId })
      : null,
    promote: !staged
      ? t("secrets.keyring.reason.noStaged")
      : !staged.loadedInProcess
        ? t("secrets.keyring.reason.stagedNotLoaded", { id: stagedId })
        : null,
    discard: staged ? null : t("secrets.keyring.reason.noStaged"),
  }
}

/** What a key's row offers: each action with why it is unavailable, and a note when it offers none. */
export interface KeyringRowCapabilities {
  reinstate: string | null
  retire: string | null
  note: string | null
}

export function keyringRowCapabilities(
  entry: HmacKeyringEntry,
  t: Translate,
  now: Date = new Date()
): KeyringRowCapabilities {
  const state = stateOf(entry)
  if (state === "Current") {
    return { reinstate: null, retire: null, note: t("secrets.keyring.reason.current") }
  }
  if (state === "Next") {
    return { reinstate: null, retire: null, note: t("secrets.keyring.reason.next") }
  }

  const webhookKeys = toNumber(entry.webhookKeyCount)
  const webhookGraceOpen = stillAhead(entry.webhookGraceUntilUtc, now)

  return {
    reinstate: entry.compromised
      ? t("secrets.keyring.reason.compromised")
      : !stillAhead(entry.verifyUntilUtc, now)
        ? t("secrets.keyring.reason.windowClosed")
        : null,
    retire:
      webhookKeys > 0 && webhookGraceOpen
        ? t("secrets.keyring.reason.referenced", {
            count: webhookKeys,
            date: formatDateTime(entry.webhookGraceUntilUtc),
          })
        : null,
    note: null,
  }
}
