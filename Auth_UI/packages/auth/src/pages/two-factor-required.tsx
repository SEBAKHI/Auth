import { useQuery } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useLocation, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api, refreshSessionNow } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { MfaRequirement } from "@authsystem/api/mfa-requirement"
import type { Schemas } from "@authsystem/api/types"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { AuthLayout } from "@authsystem/ui/auth-layout"
import { useSignInOptions } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import { SecretRevealDialog } from "@authsystem/ui/common/secret-reveal-dialog"
import { OneTimeCodeField } from "@authsystem/ui/common/one-time-code-field"
import { FieldDescription, FieldGroup, FieldSeparator } from "@authsystem/ui/field"
import { Spinner } from "@authsystem/ui/spinner"

import { useAuth } from "../auth-context"
import { useFlowBranding } from "../flow-branding"
import { useLoginCompletion } from "../login-completion"
import { usePasskeyAddition } from "../passkey/add-passkey"
import { PasskeyButton } from "../passkey/passkey-button"
import {
  passkeyFailureMessage,
  proofFailureMessage,
  withRetryAfter,
} from "../passkey/passkey-errors"
import { notePasskeysOff, usePasskeysAnsweredOff } from "../passkey/passkey-offer"
import { usePasskeySignIn, type PasskeyOutcome } from "../passkey/use-passkey-sign-in"
import { browserHasWebAuthn, type AuthenticationResponseJSON } from "../passkey/webauthn"
import { ReauthenticateDialog } from "../reauthenticate-dialog"
import { markRecoveryCodeSignIn } from "../recovery-code-notice"
import { toAccountsSearch, validateReturnToUrl } from "../return-to"
import { passkeySignInEnded, requestFullProof } from "../sign-in-method"
import { TwoFactorEnrollment } from "../two-factor-enrollment"

/** Router state of the application mode, as the completion rule writes it. */
interface ApplicationModeState {
  mode?: unknown
  returnTo?: string | null
}

/**
 * One route, two modes (T-B5). The platform mode is the page as S08 built it.
 * The application mode (S23) is entered only through the completion rule, with
 * the pending authorize request in router state: an application that requires
 * two-step verification found no authenticator on the account. The request is
 * re-validated here like at every hop; without a valid one this is the
 * platform page.
 */
export function TwoFactorRequiredPage() {
  const location = useLocation()
  const state = location.state as ApplicationModeState | null
  const applicationReturnTo =
    state?.mode === "application" ? validateReturnToUrl(state.returnTo) : null

  return applicationReturnTo ? (
    <ApplicationTwoFactorRequired returnTo={applicationReturnTo} />
  ) : (
    <PlatformTwoFactorRequired />
  )
}

/**
 * The application mode (S23, AM8 (8-3)): set up an authenticator app, then
 * continue to the application. It never completes by itself and ignores the
 * platform requirement, which is "none" for every application user.
 *
 * - The account already has an authenticator (set up in another tab): it says
 *   so, and "Continue" resumes the request; the server then asks for the
 *   second step, which is progress, not a loop.
 * - Otherwise the Enroll step the platform mode uses: the emailed proof before
 *   the first factor (X02), an emailed-code factor replaced (S24), the recovery
 *   codes shown once. Then the request resumes.
 * - "Cancel" resumes it too, unmet: the server ends the request with
 *   `access_denied`, which the application shows. That is the way out, not a
 *   dead end.
 * - A demand for a fresh proof opens the window, with nothing signed out. Its way out
 *   does not sign out either, which would lose the request: the sign-in page opens
 *   with the request and the reason, and the sign-in comes back here.
 *
 * Every exit is the completion every sign-in uses. Router state here holds the
 * request without its reason, so that completion resumes the application.
 */
function ApplicationTwoFactorRequired({ returnTo }: { returnTo: string }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { user } = useAuth()
  const { complete } = useLoginCompletion()
  const { layout: appHeader } = useFlowBranding({ returnTo })
  const {
    data: me,
    isPending,
    isError,
    refetch,
  } = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(api.GET("/api/v1/Users/me")),
  })
  // The words name the passkey only where the set-up step offers one (T-B8).
  const passkeyOffered = usePasskeyChoiceOffered(me) && me?.qualifyingMfa !== true

  // One resume: a second click landing before the navigation would send the
  // request again. Set synchronously, in the handler.
  const resuming = React.useRef(false)
  const resume = React.useCallback(async () => {
    if (resuming.current) return
    resuming.current = true
    // Not a sign-in: this resume leaves no sign-in marker (OI-275).
    complete({}, { endsSignIn: false })
  }, [complete])

  const signInAgain = React.useCallback(() => {
    // A sign-in by code from there proves the password too, where the account
    // has one, as after the "sign in again" dialog (OI-215).
    requestFullProof()
    navigate(
      {
        pathname: "/login",
        search: toAccountsSearch(returnTo, {
          reason: "mfa_enroll",
          method: null,
        }),
      },
      { replace: true }
    )
  }, [navigate, returnTo])

  return (
    <AuthLayout
      title={t("auth.mfaRequiredTitle")}
      subtitle={
        passkeyOffered
          ? t("auth.mfaAppRequiredSubtitleWithPasskey")
          : t("auth.mfaAppRequiredSubtitle")
      }
      footer={
        <Button
          variant="link"
          className="text-muted-foreground"
          onClick={() => void resume()}
        >
          {t("common.cancel")}
        </Button>
      }
      {...appHeader}
    >
      {isPending ? (
        <div className="flex justify-center">
          <Spinner />
        </div>
      ) : isError ? (
        <Alert variant="destructive">
          <AlertTitle>{t("auth.mfaRequiredCheckFailedTitle")}</AlertTitle>
          <AlertDescription>
            <p>{t("auth.mfaRequiredCheckFailed")}</p>
            <Button variant="secondary" size="sm" onClick={() => void refetch()}>
              {t("common.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : me.qualifyingMfa === true ? (
        <div className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">
            {/* S20: an account whose qualifying factor is a passkey has no app. */}
            {me.totpEnabled === true
              ? t("auth.mfaAppAlreadyEnrolled")
              : t("auth.mfaPasskeyAlreadyEnrolled")}
          </p>
          <Button className="w-full" onClick={() => void resume()}>
            {t("auth.mfaAppContinue")}
          </Button>
        </div>
      ) : (
        <EnrollStep
          account={{ id: user?.id, email: user?.email }}
          onDone={resume}
          onSignInAgain={signInAgain}
        />
      )}
    </AuthLayout>
  )
}

/**
 * The page between a platform administrator and the console while the server
 * withholds their platform authority (S08: `TwoFactor:EnforceForPlatformAdmins`,
 * and this session has not proved a second factor). One of three steps, as the
 * server names it:
 *
 * - enroll: set up the first factor (with the emailed code while the server asks
 *   for it — X02's flow, the profile's own component);
 * - step_up: a code from the authenticator app, or a recovery code;
 * - passkey: sign in with the passkey, the account's only second factor (S20);
 * - reauthenticate: prove the person again, which records how this session was proved.
 *
 * Every step ends the same way: a refreshed token (the authority comes back only
 * in a newly minted one), the account read again, and the completion every
 * sign-in uses — back to where the user was going. Signing out is always there.
 *
 * It starts with that same refresh too: another tab may have stepped up already.
 *
 * The account is read again without ever ending the session over it: a read that
 * fails (a 5xx, a dropped connection) shows an error with "try again" instead of
 * a requirement the page can no longer trust — never a spinner that waits for
 * nothing. A session that is really over still ends through the client.
 */
function PlatformTwoFactorRequired() {
  const { t } = useTranslation()
  const { user, mfaRequirement, rereadUser, logout } = useAuth()
  const { complete } = useLoginCompletion()
  const [checking, setChecking] = React.useState(true)
  const [checkFailed, setCheckFailed] = React.useState(false)
  const started = React.useRef(false)
  // One completion: a passkey sign-in completes with its own result (a password
  // change it asks for included), and the requirement it clears must not
  // complete a second time over it.
  const leaving = React.useRef(false)
  const completeOnce = React.useCallback(
    (result: { requiresPasswordChange?: boolean }) => {
      if (leaving.current) return
      leaving.current = true
      complete(result)
    },
    [complete]
  )

  /** True when the token was refreshed and the account read; false otherwise. */
  const recheck = React.useCallback(async (): Promise<boolean> => {
    try {
      await refreshSessionNow()
      return await rereadUser()
    } catch {
      return false
    }
  }, [rereadUser])

  const check = React.useCallback(async () => {
    setChecking(true)
    const ok = await recheck()
    setCheckFailed(!ok)
    setChecking(false)
  }, [recheck])

  React.useEffect(() => {
    if (started.current) return
    started.current = true
    void check()
  }, [check])

  // Nothing (left) to prove: the console, or wherever the user was going.
  React.useEffect(() => {
    if (!checking && !checkFailed && mfaRequirement === "none") completeOnce({})
  }, [checking, checkFailed, mfaRequirement, completeOnce])

  return (
    <AuthLayout
      title={t("auth.mfaRequiredTitle")}
      subtitle={t("auth.mfaRequiredSubtitle")}
      footer={
        <Button variant="link" className="text-muted-foreground" onClick={() => void logout()}>
          {t("common.signOut")}
        </Button>
      }
    >
      {checking ? (
        <div className="flex justify-center">
          <Spinner />
        </div>
      ) : checkFailed ? (
        <Alert variant="destructive">
          <AlertTitle>{t("auth.mfaRequiredCheckFailedTitle")}</AlertTitle>
          <AlertDescription>
            <p>{t("auth.mfaRequiredCheckFailed")}</p>
            <Button variant="secondary" size="sm" onClick={() => void check()}>
              {t("common.retry")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : mfaRequirement === "none" ? (
        <div className="flex justify-center">
          <Spinner />
        </div>
      ) : (
        <TwoFactorRequiredStep
          requirement={mfaRequirement}
          account={{ id: user?.id, email: user?.email }}
          onDone={check}
          onSignedIn={completeOnce}
        />
      )}
    </AuthLayout>
  )
}

function TwoFactorRequiredStep({
  requirement,
  account,
  onDone,
  onSignedIn,
}: {
  requirement: Exclude<MfaRequirement, "none">
  account: { id?: string | null; email?: string | null }
  onDone: () => Promise<void>
  /** A sign-in that replaced this session (the passkey step): completed once. */
  onSignedIn: (result: { requiresPasswordChange?: boolean }) => void
}) {
  switch (requirement) {
    case "enroll":
      return <EnrollStep account={account} onDone={onDone} />
    case "step_up":
      return <StepUpStep accountId={account.id} onDone={onDone} />
    case "passkey":
      return <PasskeyStep onSignedIn={onSignedIn} />
    default:
      return <ReauthenticateStep onDone={onDone} />
  }
}

function EnrollStep({
  account,
  onDone,
  onSignInAgain,
}: {
  account: { id?: string | null; email?: string | null }
  onDone: () => Promise<void>
  /** The application mode's way out of the window: the sign-in page, never a sign-out. */
  onSignInAgain?: () => void
}) {
  const { t } = useTranslation()
  const [recoveryCodes, setRecoveryCodes] = React.useState<string>()
  // The account's own factor (S24): an emailed code never satisfies the policy, so
  // such an administrator binds the app in its place, with the proof that it may
  // go. The enrollment also learns it from the server if this read fails.
  const { data: me } = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(api.GET("/api/v1/Users/me")),
  })
  const replacesEmailCode = me?.emailCodeEnabled === true

  // OI-280 PR B (T-B8, D-152-3 (a)): a passkey beside the app, while passkeys run
  // here and the addition cannot end in N9. Added through the one addition every
  // page shares, then used once in place (D-B5), and the page goes on as after the
  // app. A 404 from its options drops the choice: the page is the app's again.
  const passkeyOffered = usePasskeyChoiceOffered(me)
  const [passkeyAdded, setPasskeyAdded] = React.useState(false)
  const [passkeyMessage, setPasskeyMessage] = React.useState<string | null>(null)
  const addition = usePasskeyAddition({
    passkeyOnly: false,
    onMessage: setPasskeyMessage,
    onAdded: () => setPasskeyAdded(true),
    onSignInAgain,
    onUnavailable: notePasskeysOff,
  })

  if (passkeyAdded) {
    return <UsePasskeyStep onUsed={onDone} onSignInAgain={onSignInAgain} />
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">
        {passkeyOffered ? t("auth.mfaRequiredEnrollWithPasskey") : t("auth.mfaRequiredEnroll")}
      </p>
      {replacesEmailCode ? (
        <p className="text-sm text-muted-foreground">
          {passkeyOffered
            ? t("auth.mfaRequiredEmailCodeDoesNotCountWithPasskey")
            : t("auth.mfaRequiredEmailCodeDoesNotCount")}
        </p>
      ) : null}
      {passkeyOffered ? (
        <>
          {passkeyMessage ? (
            <Alert variant="destructive" data-testid="set-up-passkey-message">
              <AlertDescription>{passkeyMessage}</AlertDescription>
            </Alert>
          ) : null}
          <Button
            className="w-fit"
            disabled={addition.starting}
            data-testid="set-up-add-passkey"
            onClick={() => void addition.begin()}
          >
            {addition.starting ? <Spinner data-icon="inline-start" /> : null}
            {t("auth.mfaRequiredAddPasskey")}
          </Button>
          <FieldSeparator>{t("auth.or")}</FieldSeparator>
        </>
      ) : null}
      {addition.dialogs}
      <TwoFactorEnrollment
        // Re-mounted once the account is read, so the enrollment starts in the
        // replacement step rather than learning it from a refused request.
        key={replacesEmailCode ? "replaces-email-code" : "first-factor"}
        account={account}
        replacesEmailCode={replacesEmailCode}
        onSignInAgain={onSignInAgain}
        onEnabled={(codes) => {
          // The codes are shown once; the console waits until they are saved.
          if (codes.length) setRecoveryCodes(codes.join("\n"))
          else void onDone()
        }}
      />
      <SecretRevealDialog
        open={Boolean(recoveryCodes)}
        onOpenChange={(open) => {
          if (open) return
          setRecoveryCodes(undefined)
          void onDone()
        }}
        title={t("profile.recoveryCodesTitle")}
        description={t("profile.recoveryCodesBody")}
        value={recoveryCodes ?? ""}
        multiline
      />
    </div>
  )
}

/**
 * Whether the set-up step offers a passkey beside the app (OI-280 PR B, T-B8): passkeys run here
 * (the deployment's hint, and no 404 met on this page since), this browser has WebAuthn (any
 * authenticator: this device's, a phone's or a security key), and the addition cannot end in N9
 * (email sending on; the person has no factor here). Otherwise the page is the app's, as before.
 */
function usePasskeyChoiceOffered(me: Schemas["UserDto"] | undefined): boolean {
  const passkeysOffered = useSignInOptions()?.passkeySignInOffered === true
  const answeredOff = usePasskeysAnsweredOff()
  return passkeysOffered && !answeredOff && browserHasWebAuthn() && me?.emailCodeAvailable === true
}

/**
 * The passkey just added, used once in place (OI-280 PR B, T-B8, D-B5). A registration proves
 * nothing signed, so it raised no session; this proof, the window's own
 * (`auth/reauthentication/passkey`), is signed by the passkey and raises this session and its SSO
 * session. Then the page goes on as after the app: the request resumes once, or the platform page
 * refreshes the session and completes. A cancelled or refused touch leaves the passkey added and
 * the step here, with its button to try again and the page's own way out.
 */
function UsePasskeyStep({
  onUsed,
  onSignInAgain,
}: {
  onUsed: () => Promise<void> | void
  /** The application mode's way out: the sign-in page with the request, never a sign-out. */
  onSignInAgain?: () => void
}) {
  const { t } = useTranslation()

  const prove = async (credential: AuthenticationResponseJSON): Promise<PasskeyOutcome> => {
    try {
      const { error, response } = await api.POST("/api/v1/auth/reauthentication/passkey", {
        body: { credential },
      })
      if (error) throw withRetryAfter(error, response)
      void onUsed()
      return { leaving: true }
    } catch (error) {
      // The token names no live session: only a sign-in goes on, which keeps the request here.
      if (onSignInAgain && getErrorCodes(error).includes("Auth.ReauthenticationRequired")) {
        onSignInAgain()
        return { leaving: true }
      }
      return { message: proofFailureMessage(error) }
    }
  }
  // The account holds this passkey and passkeys run: a first failure is said, in the words of a
  // proof, which send a signed-in person to no sign-in.
  const ceremony = usePasskeySignIn({
    passkeysExpected: true,
    onCredential: prove,
    failureMessage: proofFailureMessage,
  })

  return (
    <div className="flex flex-col gap-4" data-testid="use-passkey-now">
      <p className="text-sm text-muted-foreground">{t("auth.mfaRequiredUsePasskey")}</p>
      <PasskeyButton
        ceremony={ceremony}
        label={t("auth.mfaRequiredUsePasskeyAction")}
        variant="default"
      />
    </div>
  )
}

function StepUpStep({
  accountId,
  onDone,
}: {
  accountId?: string | null
  onDone: () => Promise<void>
}) {
  const { t } = useTranslation()
  const [code, setCode] = React.useState("")
  const [useRecoveryCode, setUseRecoveryCode] = React.useState(false)
  const [submitting, setSubmitting] = React.useState(false)
  const [reauthenticateOpen, setReauthenticateOpen] = React.useState(false)
  // A correct code is spent by the first request; a second click before the
  // button re-renders disabled would be refused as reused.
  const submittingRef = React.useRef(false)

  const submit = async () => {
    if (!code || submittingRef.current) return
    submittingRef.current = true
    setSubmitting(true)
    try {
      const { error } = await api.POST("/api/v1/auth/2fa/step-up", {
        body: { code, useRecoveryCode },
      })
      if (error) throw error
      // A recovery code spent here is spent like one at sign-in: the security
      // page says so once, with the way to a new set (AM-S08-1).
      if (useRecoveryCode) markRecoveryCodeSignIn(accountId)
      await onDone()
    } catch (error) {
      // Keyed on the published code, never on the status: TwoFactor.LockedOut is
      // a 403 too, and only a session that cannot be upgraded signs in again.
      if (getErrorCodes(error).includes("Auth.ReauthenticationRequired")) {
        setReauthenticateOpen(true)
      } else {
        toast.error(getErrorMessage(error))
      }
      setCode("")
    } finally {
      submittingRef.current = false
      setSubmitting(false)
    }
  }

  return (
    <>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void submit()
        }}
      >
        <FieldGroup>
          <OneTimeCodeField
            // Re-mounted on the switch, so the new field takes the focus.
            key={useRecoveryCode ? "recovery" : "code"}
            id="step-up-code"
            label={useRecoveryCode ? t("auth.recoveryCode") : t("auth.twoFactorCode")}
            value={code}
            onChange={setCode}
            recovery={useRecoveryCode}
            disabled={submitting}
            autoFocus
          >
            <FieldDescription>{t("auth.mfaRequiredStepUp")}</FieldDescription>
          </OneTimeCodeField>
          <Button type="submit" className="w-full" disabled={!code || submitting}>
            {submitting ? <Spinner data-icon="inline-start" /> : null}
            {t("auth.verify")}
          </Button>
          <Button
            type="button"
            variant="link"
            className="text-muted-foreground"
            onClick={() => {
              setUseRecoveryCode((previous) => !previous)
              setCode("")
            }}
          >
            {useRecoveryCode ? t("auth.useAuthenticatorCode") : t("auth.useRecoveryCode")}
          </Button>
        </FieldGroup>
      </form>

      {/* Mounted only while needed, so closing it resets it. */}
      {reauthenticateOpen ? (
        <ReauthenticateDialog
          open
          onOpenChange={setReauthenticateOpen}
          description={t("auth.mfaRequiredReauthenticateBody")}
          onProved={() => void onDone()}
        />
      ) : null}
    </>
  )
}

/**
 * The account's only qualifying second factor is a passkey (S20, `mfa_req`
 * "passkey"): nothing upgrades this session, a passkey sign-in replaces it with
 * one that proved two factors. That sign-in is adopted like any other and the
 * page leaves through the completion every sign-in uses — never a refresh,
 * which would only renew the session that lacks the factor (TS10). A passkey
 * of another account signs that account in. The session it replaces ends on
 * its own (FA5).
 *
 * A lost passkey is named with its way out (M8(4)): another administrator
 * resets the account's two-step verification, then the account sets one up.
 * While passkeys do not run here the button is not shown, and that way out is
 * all the step says (the server says why, and a deployment that switches
 * passkeys off has this one way left).
 */
function PasskeyStep({
  onSignedIn,
}: {
  onSignedIn: (result: { requiresPasswordChange?: boolean }) => void
}) {
  const { t } = useTranslation()
  const { loginWithPasskey } = useAuth()

  const signIn = async (credential: AuthenticationResponseJSON): Promise<PasskeyOutcome> => {
    try {
      const result = await loginWithPasskey(credential, false)
      if (result.status !== "authenticated") {
        return { message: t("auth.mfaRequiredCheckFailed") }
      }
      passkeySignInEnded()
      onSignedIn(result)
      return { leaving: true }
    } catch (error) {
      return { message: passkeyFailureMessage(error) }
    }
  }
  // The requirement says passkeys run here: a first failure is said, with a retry.
  const passkey = usePasskeySignIn({ onCredential: signIn, passkeysExpected: true })

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("auth.mfaRequiredPasskey")}</p>
      <PasskeyButton
        ceremony={passkey}
        label={t("auth.passkey.signInWithYourPasskey")}
        variant="default"
      />
      <p className="text-sm text-muted-foreground" data-testid="passkey-lost-way-out">
        {t("auth.mfaRequiredPasskeyLost")}
      </p>
    </div>
  )
}

function ReauthenticateStep({ onDone }: { onDone: () => Promise<void> }) {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("auth.mfaRequiredReauthenticate")}</p>
      <Button className="w-full" onClick={() => setOpen(true)}>
        {t("auth.reauthenticate.confirm")}
      </Button>
      {open ? (
        <ReauthenticateDialog
          open
          onOpenChange={setOpen}
          description={t("auth.mfaRequiredReauthenticateBody")}
          onProved={() => void onDone()}
        />
      ) : null}
    </div>
  )
}
