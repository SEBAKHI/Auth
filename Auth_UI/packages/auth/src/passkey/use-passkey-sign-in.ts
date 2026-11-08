import * as React from "react"

import { api } from "@authsystem/api/client"
import { parseApiInstant } from "@authsystem/ui/api-instant"

import {
  isPasskeyFailureRetryable,
  passkeyFailureMessage,
  retryDelayMs,
  withRetryAfter,
} from "./passkey-errors"
import {
  browserHasWebAuthn,
  isCeremonyCancelled,
  loadWebAuthn,
  type AuthenticationResponseJSON,
  type PublicKeyCredentialRequestOptionsJSON,
  type WebAuthnHelper,
} from "./webauthn"

/** What the page made of the browser's answer. */
export interface PasskeyOutcome {
  /** What to say under the button; absent or null for nothing. */
  message?: string | null
  /** The page is leaving (it adopted a session, or went elsewhere): nothing more is fetched. */
  leaving?: boolean
}

/**
 * - `unavailable` — this browser has no WebAuthn, or passkeys do not run (their
 *   options answered 404): nothing is shown, and nothing changes on the page;
 * - `checking` — nothing has said yet that passkeys run here (the first answer
 *   is on its way, or it failed): nothing is shown either, so a page whose
 *   deployment does not run passkeys never shows a control, even while the API
 *   cannot be reached;
 * - `preparing` — passkeys run here, and fresh options or the helper are on
 *   their way: the button is there, disabled;
 * - `ready` — a click starts the ceremony;
 * - `busy` — a ceremony, or the request that checks it, is in flight;
 * - `failed` — the options could not be had: a sentence, and a way to try again.
 */
export type PasskeyPhase = "unavailable" | "checking" | "preparing" | "ready" | "busy" | "failed"

export interface PasskeyCeremony {
  phase: PasskeyPhase
  message: string | null
  /** Whether the person may try again by themself (a fault, not a refusal or a wait). */
  retryable: boolean
  /** The address field's passkey suggestion runs: the field says `username webauthn`. */
  suggestionSupported: boolean
  /** Starts the ceremony. Called from the click handler itself, synchronously (T-C2). */
  start: () => void
  /** Fetches fresh options after a failure. */
  retry: () => void
  /**
   * The page learned that passkeys run here (a door answered that the account
   * signs in with its passkey): options still missing are asked for at once,
   * and a failure is said from now on.
   */
  confirm: () => void
}

interface Prepared {
  options: PublicKeyCredentialRequestOptionsJSON
  expiresAt: string
}

/**
 * The options are renewed a minute before their challenge expires (T-C3): the
 * server offers the same ones again while more than a minute is left, then new
 * ones. Bounded on both sides, so a device whose clock disagrees with the
 * server's renews every half minute at worst, never in a loop, and never later
 * than the five-minute challenge allows.
 */
const RENEW_BEFORE_MS = 60_000
const MIN_RENEW_DELAY_MS = 30_000
const MAX_RENEW_DELAY_MS = 4 * 60_000

function renewDelayMs(expiresAt: string): number {
  const at = parseApiInstant(expiresAt)?.getTime()
  if (at === undefined || Number.isNaN(at)) return MIN_RENEW_DELAY_MS
  return Math.min(MAX_RENEW_DELAY_MS, Math.max(MIN_RENEW_DELAY_MS, at - RENEW_BEFORE_MS - Date.now()))
}

function clearTimer(timer: React.RefObject<number | null>) {
  if (timer.current !== null) {
    window.clearTimeout(timer.current)
    timer.current = null
  }
}

/** A renewal is put off while a ceremony runs: new options would replace its challenge. */
const BUSY_RECHECK_MS = 5_000

/** The options of a passkey sign-in, or "off" when passkeys do not run. */
async function fetchAssertionOptions(): Promise<Prepared | "off"> {
  const { data, error, response } = await api.POST("/api/v1/auth/passkeys/assertion/options")
  // T-C4: only this 404 means «passkeys are off». Any other failure is said.
  if (response.status === 404) return "off"
  if (error || !data) throw withRetryAfter(error ?? new Error("Passkey options failed"), response)
  return {
    options: data.options as PublicKeyCredentialRequestOptionsJSON,
    expiresAt: data.expiresAt,
  }
}

/**
 * A passkey sign-in on a page (S20 PR C, PK15): the sign-in page's button and
 * address-field suggestion, the two-factor page's passkey step, and the recovery
 * page's passkey choice. The page says what the browser's answer is for
 * (`onCredential`: a sign-in, or a recovery); this owns the rest.
 *
 * - The options are fetched when the page mounts, and the helper loaded once
 *   they answer 200 on a browser with `PublicKeyCredential`: the button stays
 *   disabled until both are in hand, so the click never awaits anything before
 *   the ceremony, which a browser grants only inside the click (T-C2).
 * - They are renewed a minute before they expire, and after every answer was
 *   checked, since the check spends their challenge (T-C3). Each renewal
 *   restarts the suggestion with the new ones.
 * - Only a 404 from the options means passkeys do not run (T-C4). A cancelled
 *   ceremony says nothing; a fault says so and offers to try again; a 429 says
 *   how long to wait, and the options are asked for again after it. Until the
 *   server has shown that passkeys run here (options answered 200, or the page
 *   expects them: `passkeysExpected`, `confirm`), a failure says nothing and
 *   leaves the page as it was, and the options are asked for again later: a
 *   deployment without passkeys whose API cannot be reached for a moment must
 *   not show a passkey control.
 * - The suggestion (`suggestion: true`) runs only where the browser offers it
 *   (`browserSupportsWebAuthnAutofill`), from mount, and is aborted when the
 *   button starts its own ceremony, at each renewal and on unmount (T-C5).
 */
export function usePasskeySignIn({
  suggestion = false,
  passkeysExpected = false,
  onCredential,
}: {
  /** Offer the passkeys in the address field's suggestions too (the sign-in page). */
  suggestion?: boolean
  /**
   * The server already said passkeys run here (the two-factor page's passkey
   * requirement, the sign-in page under `mfa_passkey`): a first failure is said.
   */
  passkeysExpected?: boolean
  /** Sends the browser's answer; never throws. */
  onCredential: (credential: AuthenticationResponseJSON) => Promise<PasskeyOutcome>
}): PasskeyCeremony {
  const [phase, setPhase] = React.useState<PasskeyPhase>(() =>
    browserHasWebAuthn() ? "checking" : "unavailable"
  )
  const [message, setMessage] = React.useState<string | null>(null)
  const [retryable, setRetryable] = React.useState(false)
  const [suggestionSupported, setSuggestionSupported] = React.useState(false)
  // Bumped with every set of options in hand, so the suggestion restarts with them.
  const [preparedVersion, setPreparedVersion] = React.useState(0)

  const helperRef = React.useRef<WebAuthnHelper | null>(null)
  const preparedRef = React.useRef<Prepared | null>(null)
  // Set synchronously in the click: a second click lands before the re-render.
  const busyRef = React.useRef(false)
  const mountedRef = React.useRef(true)
  const renewTimerRef = React.useRef<number | null>(null)
  // Whether the server has shown that passkeys run here: until then a failure
  // says nothing. Set by a 200, by the page's own knowledge, or by a retry.
  const knownRef = React.useRef(passkeysExpected)
  // The message on screen is the options' failure, so their next answer clears it.
  const failureShownRef = React.useRef(false)
  // A request for options is on its way: `confirm` waits for its answer.
  const fetchingRef = React.useRef(false)
  // The latest prepare, for the timers it sets for itself.
  const prepareRef = React.useRef<() => Promise<void>>(async () => {})
  // The latest page callback, so a ceremony started earlier hands its answer to it.
  const onCredentialRef = React.useRef(onCredential)
  React.useEffect(() => {
    onCredentialRef.current = onCredential
  }, [onCredential])

  const prepare = React.useCallback(async () => {
    clearTimer(renewTimerRef)
    // New options while the person is in a ceremony would replace the challenge
    // its answer is signing: the renewal waits for the ceremony to end.
    if (busyRef.current) {
      renewTimerRef.current = window.setTimeout(() => void prepareRef.current(), BUSY_RECHECK_MS)
      return
    }
    let prepared: Prepared | "off"
    fetchingRef.current = true
    try {
      prepared = await fetchAssertionOptions()
    } catch (error) {
      fetchingRef.current = false
      if (!mountedRef.current) return
      preparedRef.current = null
      // Before anything said passkeys run here, the page stays as it was: the
      // failure may be a deployment without passkeys that cannot be reached.
      if (knownRef.current) {
        failureShownRef.current = true
        setPhase("failed")
        setMessage(passkeyFailureMessage(error))
        setRetryable(isPasskeyFailureRetryable(error))
      }
      // A wait the server asked for, or a fault: asked again by itself later.
      renewTimerRef.current = window.setTimeout(() => void prepareRef.current(), retryDelayMs(error))
      return
    }
    fetchingRef.current = false
    if (!mountedRef.current) return
    if (prepared === "off") {
      preparedRef.current = null
      setPhase("unavailable")
      return
    }
    knownRef.current = true
    if (failureShownRef.current) {
      failureShownRef.current = false
      setMessage(null)
      setRetryable(false)
    }

    let helper = helperRef.current
    if (!helper) {
      try {
        helper = await loadWebAuthn()
      } catch (error) {
        if (!mountedRef.current) return
        setPhase("failed")
        setMessage(passkeyFailureMessage(error))
        setRetryable(true)
        return
      }
      if (!mountedRef.current) return
      helperRef.current = helper
      if (suggestion) {
        const supported = await helper.browserSupportsWebAuthnAutofill().catch(() => false)
        if (!mountedRef.current) return
        setSuggestionSupported(supported)
      }
    }

    preparedRef.current = prepared
    setPreparedVersion((version) => version + 1)
    if (!busyRef.current) setPhase("ready")
    renewTimerRef.current = window.setTimeout(() => void prepareRef.current(), renewDelayMs(prepared.expiresAt))
  }, [suggestion])

  React.useEffect(() => {
    prepareRef.current = prepare
  }, [prepare])

  React.useEffect(() => {
    mountedRef.current = true
    // Across an async boundary, as the session bootstrap does: the first answer
    // must not render from inside the effect that asked for it.
    const first = browserHasWebAuthn() ? window.setTimeout(() => void prepare(), 0) : null
    return () => {
      mountedRef.current = false
      if (first !== null) window.clearTimeout(first)
      clearTimer(renewTimerRef)
      // Leaves no ceremony behind: the suggestion, or a button ceremony in flight.
      helperRef.current?.WebAuthnAbortService.cancelCeremony()
    }
  }, [prepare])

  // The answer was sent and checked: its challenge is spent either way.
  const settle = React.useCallback(
    (outcome: PasskeyOutcome) => {
      busyRef.current = false
      if (!mountedRef.current) return
      setMessage(outcome.message ?? null)
      setRetryable(false)
      if (outcome.leaving) return
      setPhase("preparing")
      void prepare()
    },
    [prepare]
  )

  // A ceremony that ended without an answer: the options are still good.
  const abandon = React.useCallback((error: unknown) => {
    busyRef.current = false
    if (!mountedRef.current) return
    setMessage(passkeyFailureMessage(error))
    setRetryable(false)
    setPhase(preparedRef.current ? "ready" : "preparing")
    // Restart the suggestion, which the ceremony aborted.
    setPreparedVersion((version) => version + 1)
  }, [])

  // The address field's suggestion: from mount, restarted with every renewal.
  React.useEffect(() => {
    if (!suggestion || !suggestionSupported || phase !== "ready") return
    const helper = helperRef.current
    const prepared = preparedRef.current
    if (!helper || !prepared) return
    let current = true
    helper
      .startAuthentication({ optionsJSON: prepared.options, useBrowserAutofill: true })
      .then(async (credential) => {
        if (!current || !mountedRef.current || busyRef.current) return
        busyRef.current = true
        setPhase("busy")
        setMessage(null)
        settle(await onCredentialRef.current(credential))
      })
      .catch((error: unknown) => {
        // Aborted by a renewal, by the button or by leaving: nothing to say. A
        // suggestion the browser declined to show is no failure either.
        if (!current || isCeremonyCancelled(error)) return
      })
    return () => {
      current = false
    }
  }, [suggestion, suggestionSupported, phase, preparedVersion, settle])

  const start = React.useCallback(() => {
    const helper = helperRef.current
    const prepared = preparedRef.current
    if (!helper || !prepared || busyRef.current) return
    busyRef.current = true
    // Called before anything is awaited: the browser grants the ceremony to this
    // click alone. Starting it aborts the suggestion (the helper's abort service).
    const ceremony = helper.startAuthentication({ optionsJSON: prepared.options })
    setPhase("busy")
    setMessage(null)
    void ceremony.then(
      async (credential) => settle(await onCredentialRef.current(credential)),
      abandon
    )
  }, [abandon, settle])

  const retry = React.useCallback(() => {
    if (busyRef.current) return
    knownRef.current = true
    failureShownRef.current = false
    setPhase("preparing")
    setMessage(null)
    void prepare()
  }, [prepare])

  const confirm = React.useCallback(() => {
    if (knownRef.current) return
    knownRef.current = true
    // Options in hand already, or on their way: their answer, or the next
    // failure, is said, and nothing more is asked.
    if (busyRef.current || preparedRef.current || fetchingRef.current) return
    setPhase("preparing")
    setMessage(null)
    void prepare()
  }, [prepare])

  return { phase, message, retryable, suggestionSupported, start, retry, confirm }
}
