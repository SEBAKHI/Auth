import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { AuthenticatorApps } from "@authsystem/ui/common/authenticator-apps"
import { CopyButton } from "@authsystem/ui/common/copy-button"
import { QrCode } from "@authsystem/ui/common/qr-code"
import { VerifyEmailDialog } from "@authsystem/ui/common/verify-email-dialog"
import { Field, FieldDescription, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from "@authsystem/ui/input-group"
import { Spinner } from "@authsystem/ui/spinner"

import { ReauthenticateDialog } from "./reauthenticate-dialog"
import { STALE_TWO_FACTOR_CODES } from "./two-factor-codes"

/**
 * Whole minutes until an emailed code expires, by the server's expiry. Bounded
 * to the range the server allows (Email:OtpExpirationMinutes is 1-60), so a
 * badly set device clock cannot print a nonsense number; undefined when the
 * answer carried no expiry.
 */
function minutesUntil(
  expiresAt: string | null | undefined
): number | undefined {
  const milliseconds = expiresAt ? Date.parse(expiresAt) - Date.now() : NaN
  if (!Number.isFinite(milliseconds)) return undefined
  return Math.min(60, Math.max(1, Math.round(milliseconds / 60_000)))
}

/**
 * A new secret, as an authenticator app is given it: the apps to install, the QR
 * code, and the key for typing in by hand. One panel for every place a secret is
 * handed out — the first factor (below) and a replacement authenticator (the
 * profile's security tab).
 */
export function AuthenticatorKeyPanel({
  secret,
  description,
}: {
  secret: Pick<Schemas["TwoFactorSetupResponse"], "qrCodeUri" | "manualEntryKey">
  description: string
}) {
  const { t } = useTranslation()

  return (
    <>
      <p className="text-sm text-muted-foreground">{description}</p>
      {/* Above the QR: the app has to exist before the code is any use. */}
      <AuthenticatorApps />
      <div className="flex justify-center">
        <QrCode value={secret.qrCodeUri} />
      </div>
      <Field>
        <FieldLabel htmlFor="manual-entry-key">
          {t("profile.manualEntry")}
        </FieldLabel>
        <div className="flex items-center gap-2">
          {/* A base32 secret typed into an authenticator by hand — pinned
              LTR so an RTL profile cannot right-align or reorder it. */}
          <Input
            id="manual-entry-key"
            readOnly
            dir="ltr"
            value={secret.manualEntryKey}
            className="font-mono text-xs"
          />
          <CopyButton value={secret.manualEntryKey} />
        </div>
      </Field>
    </>
  )
}

/** The account whose first factor is being set up. */
export interface TwoFactorEnrollmentAccount {
  id?: string | null
  email?: string | null
  /** Undefined when the caller does not know; the server then says so on send. */
  emailConfirmed?: boolean | null
}

/**
 * Switching two-factor authentication on: setup (the QR code and the key), the
 * code emailed to the confirmed address while the server asks for it (X02), and
 * the authenticator code that enables it.
 *
 * One implementation, used by the profile's security tab and by the page that
 * stands between a platform administrator without a factor and the console
 * (S08). It lives in this package because the account package depends on it, not
 * the other way round.
 *
 * Enabling also upgrades the session it runs in on the server, so a caller that
 * needs the new authority refreshes its token once {@link onEnabled} runs.
 */
export function TwoFactorEnrollment({
  account,
  onEnabled,
  onStale,
}: {
  account: TwoFactorEnrollmentAccount
  /** The factor is on: the recovery codes to show the user once, never again. */
  onEnabled: (recoveryCodes: string[]) => void
  /** The caller's picture of two-factor is out of date: refetch it. */
  onStale?: () => void
}) {
  const { t } = useTranslation()

  const [setup, setup_set] = React.useState<Schemas["TwoFactorSetupResponse"]>()
  const [code, setCode] = React.useState("")
  const [reauthenticateOpen, setReauthenticateOpen] = React.useState(false)
  // The account's FIRST factor, while email is on, also needs a code sent to its
  // address: shown when setup says so, or when enable answers that it is needed.
  const [emailStep, setEmailStep] = React.useState(false)
  const [emailCode, setEmailCode] = React.useState("")
  const [emailCodeSent, setEmailCodeSent] = React.useState<{
    sentTo: string
    minutes?: number
  }>()
  // Every send mails a new code and retires the previous one, so a second click
  // landing before the button re-renders disabled would make the first email's
  // code useless: the latch is set synchronously, in the handler. Enable is not
  // repeatable either — a second request finds the emailed code spent by the
  // first — and a send during an enable retires the code being checked, so the
  // two latches also keep each other out.
  const sendingEmailCode = React.useRef(false)
  const enabling = React.useRef(false)
  // The code goes only to a confirmed address. A provider sign-in can link an
  // account whose own address was never confirmed, so the step offers to
  // confirm it first rather than a send that can only be refused.
  const [recipientUnavailable, setRecipientUnavailable] = React.useState(false)
  const [confirmAddressOpen, setConfirmAddressOpen] = React.useState(false)
  const addressUnconfirmed =
    Boolean(account.id && account.email) &&
    (account.emailConfirmed === false || recipientUnavailable)

  const clearSetup = () => {
    setup_set(undefined)
    setCode("")
    setEmailStep(false)
    setEmailCode("")
    setEmailCodeSent(undefined)
    setRecipientUnavailable(false)
  }

  /*
   * One answer to a failure for setup, the email code and enable, keyed by the
   * published code — never by the status: TwoFactor.LockedOut is a 403 as well,
   * and only a stale sign-in can be fixed by signing in again.
   */
  const onTwoFactorError = (error: unknown) => {
    const codes = getErrorCodes(error)
    if (codes.includes("Auth.ReauthenticationRequired")) {
      setReauthenticateOpen(true)
      return
    }

    toast.error(getErrorMessage(error))

    // A setting changed after setup: the server now wants the emailed code.
    if (codes.includes("TwoFactor.EmailCodeRequired")) {
      setEmailStep(true)
    }

    // The address was not confirmed after all: offer to confirm it.
    if (codes.includes("TwoFactor.EmailCodeRecipientUnavailable")) {
      setRecipientUnavailable(true)
    }

    if (STALE_TWO_FACTOR_CODES.some((stale) => codes.includes(stale))) {
      clearSetup()
      onStale?.()
    }
  }

  const setupMutation = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/2fa/setup")),
    onSuccess: (data) => {
      setup_set(data)
      setCode("")
      // An API without the member (an older build) needs no email step.
      setEmailStep(data?.emailCodeRequired === true)
      setEmailCode("")
      setEmailCodeSent(undefined)
    },
    onError: onTwoFactorError,
  })

  const sendEmailCodeMutation = useMutation({
    mutationFn: () => unwrap(api.POST("/api/v1/auth/2fa/email-code")),
    onSuccess: (data) => {
      // Email was switched off, or the rule, since setup: no code is needed.
      if (data?.emailCodeRequired !== true) {
        setEmailStep(false)
        setEmailCodeSent(undefined)
        return
      }
      setEmailCodeSent({
        sentTo: data.sentTo ?? "",
        minutes: minutesUntil(data.expiresAt),
      })
      // The send retired any earlier code, so one typed from an older email
      // could only fail — and count against the five tries.
      setEmailCode("")
    },
    onError: onTwoFactorError,
  })

  const sendEmailCode = () => {
    if (sendingEmailCode.current || enabling.current) return
    sendingEmailCode.current = true
    sendEmailCodeMutation.mutate(undefined, {
      onSettled: () => {
        sendingEmailCode.current = false
      },
    })
  }

  const enableMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/auth/2fa/enable", {
          body: emailStep ? { code, emailCode } : { code },
        })
      ),
    onSuccess: (data) => {
      toast.success(t("profile.twoFactorEnabledToast"))
      clearSetup()
      onEnabled(data?.recoveryCodes ?? [])
    },
    onError: onTwoFactorError,
  })

  const enable = () => {
    if (enabling.current || sendingEmailCode.current) return
    enabling.current = true
    enableMutation.mutate(undefined, {
      onSettled: () => {
        enabling.current = false
      },
    })
  }

  return (
    <>
      {setup ? (
        <div className="flex max-w-md flex-col gap-3">
          <AuthenticatorKeyPanel
            secret={setup}
            description={t("profile.setupTwoFactorBody")}
          />
          {emailStep && addressUnconfirmed ? (
            <Alert>
              <AlertTitle>
                {t("profile.twoFactorEmailUnconfirmedTitle")}
              </AlertTitle>
              <AlertDescription>
                <p>{t("profile.twoFactorEmailUnconfirmed")}</p>
                {/* secondary, not outline: outline sets no text colour and
                    would inherit the description's muted one, reading as
                    disabled. */}
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setConfirmAddressOpen(true)}
                >
                  {t("profile.twoFactorEmailConfirm")}
                </Button>
              </AlertDescription>
            </Alert>
          ) : null}
          {emailStep && !addressUnconfirmed ? (
            <Field>
              <FieldLabel htmlFor="enable-email-code">
                {t("profile.twoFactorEmailCode")}
              </FieldLabel>
              <InputGroup>
                {/* "one-time-code" stays on the app-code field alone: with
                    two, a password manager filling the authenticator code
                    picks the first one, which is this. */}
                <InputGroupInput
                  id="enable-email-code"
                  value={emailCode}
                  onChange={(e) => setEmailCode(e.target.value)}
                  inputMode="numeric"
                  autoComplete="off"
                />
                <InputGroupAddon align="inline-end">
                  <InputGroupButton
                    onClick={sendEmailCode}
                    disabled={
                      sendEmailCodeMutation.isPending ||
                      enableMutation.isPending
                    }
                  >
                    {sendEmailCodeMutation.isPending ? (
                      <Spinner data-icon="inline-start" />
                    ) : null}
                    {emailCodeSent
                      ? t("profile.twoFactorEmailCodeResend")
                      : t("profile.twoFactorEmailCodeSend")}
                  </InputGroupButton>
                </InputGroupAddon>
              </InputGroup>
              {/* Says where the code went — the address it was sent to — so a
                  code that does not arrive can be told from one sent elsewhere. */}
              <FieldDescription aria-live="polite">
                {emailCodeSent
                  ? [
                      t("profile.twoFactorEmailCodeSentTo", {
                        email: emailCodeSent.sentTo,
                      }),
                      emailCodeSent.minutes === undefined
                        ? null
                        : t("profile.twoFactorEmailCodeExpiresIn", {
                            minutes: emailCodeSent.minutes,
                          }),
                    ]
                      .filter(Boolean)
                      .join(" ")
                  : t("profile.twoFactorEmailCodeHint")}
              </FieldDescription>
            </Field>
          ) : null}
          <div className="flex items-end gap-2">
            <Field className="flex-1">
              <FieldLabel htmlFor="enable-code">
                {t("auth.twoFactorCode")}
              </FieldLabel>
              <Input
                id="enable-code"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                inputMode="numeric"
                autoComplete="one-time-code"
              />
            </Field>
            <Button
              onClick={enable}
              disabled={
                !code ||
                (emailStep && (addressUnconfirmed || !emailCode)) ||
                enableMutation.isPending ||
                sendEmailCodeMutation.isPending
              }
            >
              {enableMutation.isPending ? <Spinner /> : null}
              {t("auth.verify")}
            </Button>
          </div>
        </div>
      ) : (
        <Button
          className="w-fit"
          onClick={() => setupMutation.mutate()}
          disabled={setupMutation.isPending}
        >
          {setupMutation.isPending ? <Spinner /> : null}
          {t("profile.enableTwoFactor")}
        </Button>
      )}

      {/* Mounted only while needed, so closing it resets it. */}
      {reauthenticateOpen ? (
        <ReauthenticateDialog open onOpenChange={setReauthenticateOpen} />
      ) : null}

      {/* The account's own address, confirmed through the user-id path: it
          answers 204 and signs nobody in a second time. */}
      {confirmAddressOpen && account.id && account.email ? (
        <VerifyEmailDialog
          open
          onOpenChange={setConfirmAddressOpen}
          email={account.email}
          userId={account.id}
          onVerified={() => {
            setRecipientUnavailable(false)
            onStale?.()
          }}
        />
      ) : null}
    </>
  )
}
