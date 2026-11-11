import { useQuery } from "@tanstack/react-query"
import { KeyRound } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { toNumber, unwrap } from "@authsystem/api/helpers"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { useSignInOptions } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import { Spinner } from "@authsystem/ui/spinner"

import { useFlowBranding } from "../flow-branding"
import { useLoginCompletion, type PasskeyOfferExit } from "../login-completion"
import { usePasskeyAddition } from "../passkey/add-passkey"
import {
  clearPasskeyOfferNavigation,
  declinePasskeyOffer,
  notePasskeysOff,
  passkeyOfferDeclined,
  usePasskeysAnsweredOff,
} from "../passkey/passkey-offer"
import { platformAuthenticatorAvailable } from "../passkey/webauthn"
import { FullScreenLoader } from "../require-auth"
import { toAccountsSearch } from "../return-to"
import type { SignInProof } from "../sign-in-marker"

function isProof(value: unknown): value is SignInProof {
  return value === "password" || value === "code" || value === "external"
}

/** What the completion rule carried here, read again rather than trusted. */
function readExit(state: unknown): PasskeyOfferExit {
  const raw = (state as { passkeyOffer?: { proof?: unknown; launchUrl?: unknown } } | null)
    ?.passkeyOffer
  return {
    proof: isProof(raw?.proof) ? raw.proof : null,
    // Checked by the launch rule before it is ever followed (OI-192).
    launchUrl: typeof raw?.launchUrl === "string" ? raw.launchUrl : null,
  }
}

/**
 * The offer to add a passkey, between a sign-in and where it goes (OI-280 PR B: owner decisions
 * D-S20-12, Q1 (a), Q2 (a)). The completion rule opens it after a sign-in that did not use a
 * passkey, while passkeys may run and this browser can use one; this page decides the rest before
 * it shows anything, and while it decides there is only a spinner:
 *
 * - this device can make a passkey of its own (a built-in fingerprint, face or PIN, D-B6): a
 *   computer without one would only be sent to a phone's QR code;
 * - the account holds none, and adding one cannot end in N9 (email off with no factor), read from
 *   the account itself (`/Users/me`, D-B9).
 *
 * Any of them false, or a read that fails, and the sign-in goes on at once, as if no offer existed.
 * Otherwise the screen: what a passkey is, «Add a passkey», «Not now» (nothing stored, the next
 * sign-in asks again) and «Don't ask on this device» (one browser-wide flag, D-B7). «Add» runs the
 * one addition every page shares, so the window and the emailed code come as the server's gate
 * asks; a registration raises no session (D-B5), and nothing waits on it here. Every way out is the
 * completion every sign-in uses, with the sign-in's own proof: the marker it leaves is the one the
 * sign-in would have left (OI-275), and the offer never opens twice for one sign-in.
 */
export function PasskeyOfferPage() {
  const { t } = useTranslation()
  const location = useLocation()
  const navigate = useNavigate()
  const { complete, returnTo, reason } = useLoginCompletion()
  const { layout: appHeader } = useFlowBranding({ returnTo })
  const exit = readExit(location.state)
  const { proof, launchUrl } = exit

  // Arrived: the sign-in pages' guard has nothing left to follow here.
  React.useEffect(() => clearPasskeyOfferNavigation(), [])

  // One way out per arrival, whichever comes first: a click and a decision landing together leave
  // once. Keyed by the arrival, so a later navigation to this page can leave again.
  const leftForRef = React.useRef<string | null>(null)
  const leave = React.useCallback(() => {
    if (leftForRef.current === location.key) return
    leftForRef.current = location.key
    complete({}, { proof: proof ?? undefined, launchUrl })
  }, [complete, proof, launchUrl, location.key])

  // Passkeys may still run here: the deployment's word, and no 404 met on this page since. A
  // page reached without them (typed in, or switched off a moment ago) goes on at once.
  const signInOptions = useSignInOptions()
  const answeredOff = usePasskeysAnsweredOff()
  // «Don't ask on this device», read once: the completion asks it before it comes here, and a
  // page reached another way (typed in) honours it too.
  const [declined] = React.useState(passkeyOfferDeclined)
  const passkeysOff =
    declined ||
    answeredOff ||
    (signInOptions !== null && signInOptions.passkeySignInOffered !== true)

  // e: this device can make a passkey of its own.
  const [deviceCanMake, setDeviceCanMake] = React.useState<boolean | null>(null)
  React.useEffect(() => {
    let current = true
    void platformAuthenticatorAvailable().then((available) => {
      if (current) setDeviceCanMake(available)
    })
    return () => {
      current = false
    }
  }, [])

  // f, g: the account's own side, read afresh for this page, under a key of its own: the
  // decision is made once, and a later refresh of the account (the addition's, after email went
  // off) never takes the screen away from under a sentence the person is reading.
  const me = useQuery({
    queryKey: ["me", "passkey-offer"],
    queryFn: () => unwrap(api.GET("/api/v1/Users/me")),
    enabled: deviceCanMake === true && !passkeysOff,
    refetchOnMount: "always",
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })
  const account = me.isFetchedAfterMount && me.isSuccess ? me.data : null
  const passkeyCount = account?.passkeyCount
  const offered =
    !passkeysOff &&
    deviceCanMake === true &&
    account !== null &&
    passkeyCount !== null &&
    passkeyCount !== undefined &&
    toNumber(passkeyCount) === 0 &&
    (account.emailCodeAvailable === true || account.qualifyingMfa === true)
  const decidedAgainst =
    passkeysOff || deviceCanMake === false || (me.isFetchedAfterMount && !offered)
  React.useEffect(() => {
    if (decidedAgainst) leave()
  }, [decidedAgainst, leave])

  const [message, setMessage] = React.useState<string | null>(null)

  // The window's way out keeps an application's request: the sign-in page, with it.
  const signInAgain = React.useCallback(() => {
    if (!returnTo) return
    navigate(
      { pathname: "/login", search: toAccountsSearch(returnTo, reason) },
      { replace: true }
    )
  }, [navigate, returnTo, reason])

  const addition = usePasskeyAddition({
    passkeyOnly: false,
    onMessage: setMessage,
    onAdded: () => {
      toast.success(t("auth.passkeyOffer.added"))
      leave()
    },
    // A refusal, or a device that was cancelled: the screen stays, and says so.
    onClosed: () => setMessage(t("auth.passkeyOffer.notAdded")),
    onSignInAgain: returnTo ? signInAgain : undefined,
    // Passkeys switched off since the hint said they run (the options' 404): nothing here can
    // succeed, so the page goes on, as it does when it learns that before showing anything.
    onUnavailable: notePasskeysOff,
  })

  if (!offered) return <FullScreenLoader />

  return (
    <AuthLayout
      title={t("auth.passkeyOffer.title")}
      subtitle={t("auth.passkeyOffer.body")}
      {...appHeader}
    >
      <div className="flex flex-col gap-3" data-testid="passkey-offer">
        {message ? (
          <Alert data-testid="passkey-offer-message">
            <AlertDescription>{message}</AlertDescription>
          </Alert>
        ) : null}
        <Button
          className="w-full"
          disabled={addition.starting}
          onClick={() => void addition.begin()}
        >
          {addition.starting ? (
            <Spinner data-icon="inline-start" />
          ) : (
            <KeyRound data-icon="inline-start" />
          )}
          {t("auth.passkeyOffer.add")}
        </Button>
        <Button variant="outline" className="w-full" onClick={leave}>
          {t("auth.passkeyOffer.notNow")}
        </Button>
        <Button
          variant="link"
          className="w-full text-muted-foreground"
          onClick={() => {
            declinePasskeyOffer()
            leave()
          }}
        >
          {t("auth.passkeyOffer.dontAsk")}
        </Button>
      </div>
      {addition.dialogs}
    </AuthLayout>
  )
}
