import { KeyRound } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { OneTimeCodeField } from "@authsystem/ui/common/one-time-code-field"
import { RESEND_COOLDOWN_MS } from "@authsystem/ui/common/otp-input"
import { ResendCodeButton } from "@authsystem/ui/common/resend-code-button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

import { passkeyFailureMessage, withRetryAfter } from "./passkey-errors"
import { PASSKEY_NAME_MAX_CODE_POINTS } from "./passkey-name"
import {
  isAlreadyOnThisDevice,
  isCeremonyCancelled,
  type PublicKeyCredentialCreationOptionsJSON,
  type RegistrationResponseJSON,
  type WebAuthnHelper,
} from "./webauthn"

/** A registration begun: the options, the helper, and whether the emailed code is needed. */
export interface PasskeyRegistration {
  options: PublicKeyCredentialCreationOptionsJSON
  /** On this page's own clock; null when this device's clock could not say. */
  usableUntil: number | null
  emailCodeRequired: boolean
  helper: WebAuthnHelper
}

/** A registration's options are not used in their last seconds: the click would only fail. */
const OPTIONS_MARGIN_MS = 5_000

/**
 * The second click of an addition (`usePasskeyAddition`): a name, the emailed code when the
 * server asks for one, and the confirm that starts the ceremony. The options and the helper are in
 * hand, so the confirm calls `startRegistration` before awaiting anything: a browser grants the
 * ceremony to the click alone (T-C2).
 *
 * The time a sign-in by code counts for may run out between the options and the bind (OI-280
 * PR B, T-B5, D-B4). Verify then answers `TwoFactor.EmailCodeRequired` with nothing spent: the
 * dialog holds the device's answer, emails the code at once, shows its field, and sends the same
 * answer again with the code while the challenge lives, without asking the device a second time.
 * A wrong code is typed again; past the challenge, or on any other failure, fresh options.
 */
export function AddPasskeyDialog({
  registration,
  onClose,
  onAdded,
  onStartAgain,
  onReauthenticate,
  onEmailRequired,
}: {
  registration: PasskeyRegistration
  onClose: () => void
  onAdded: (name: string) => void
  /** The registration is over (spent, expired, replaced): fresh options. */
  onStartAgain: () => void
  onReauthenticate: () => void
  /** Email went off since the options (N9): the page says so, and the dialog goes. */
  onEmailRequired: () => void
}) {
  const { t } = useTranslation()
  const [name, setName] = React.useState("")
  const [emailCode, setEmailCode] = React.useState("")
  const [sentTo, setSentTo] = React.useState<string | null>(null)
  // When a new code may be asked for (the server's per-address window).
  const [resendAt, setResendAt] = React.useState<Date | null>(null)
  const [sending, setSending] = React.useState(false)
  const sendingRef = React.useRef(false)
  const [busy, setBusy] = React.useState(false)
  const busyRef = React.useRef(false)
  const [message, setMessage] = React.useState<string | null>(null)
  const [nameError, setNameError] = React.useState<string | null>(null)
  // The registration's challenge was spent or lost: only fresh options can go on.
  const [over, setOver] = React.useState(false)
  // T-B5: the device made the passkey, and verify asked for the emailed code after all. Held, to
  // be sent again with the code.
  const [held, setHeld] = React.useState<RegistrationResponseJSON | null>(null)

  const codeNeeded = registration.emailCodeRequired || held !== null
  const canConfirm = !busy && !over && (!codeNeeded || (sentTo !== null && emailCode.length === 6))

  // Leaving the page leaves no ceremony behind: not the browser's prompt, and not
  // a late request from it (T-C3's rule, as on the sign-in pages).
  const { helper } = registration
  React.useEffect(() => () => helper.WebAuthnAbortService.cancelCeremony(), [helper])

  const sendCode = async () => {
    if (sendingRef.current || busyRef.current) return
    sendingRef.current = true
    setSending(true)
    setMessage(null)
    try {
      const { data, error, response } = await api.POST("/api/v1/auth/passkeys/registration/email-code")
      if (error || !data) throw withRetryAfter(error ?? new Error("Email code failed"), response)
      // The account gained a factor since these options asked for a code: none was
      // sent, and none will come. Fresh options say what is asked now.
      if (data.emailCodeRequired === false) {
        onStartAgain()
        return
      }
      setSentTo(data.sentTo ?? "")
      setEmailCode("")
      setResendAt(new Date(Date.now() + RESEND_COOLDOWN_MS))
    } catch (error) {
      const codes = getErrorCodes(error)
      // N6: no registration is waiting any more (another page started a sign-in
      // in this browser, or it expired). Its sentence would mislead: start again.
      if (codes.includes("TwoFactor.SetupRequired")) {
        setHeld(null)
        setOver(true)
        setMessage(t("profile.passkeys.startAgain"))
      } else if (codes.includes("Auth.ReauthenticationRequired")) {
        onReauthenticate()
      } else if (codes.includes("Passkey.EmailRequired")) {
        onEmailRequired()
      } else {
        setMessage(passkeyFailureMessage(error))
      }
    } finally {
      sendingRef.current = false
      setSending(false)
    }
  }

  /** The device's answer, to the server; `resent` when it is the held one, sent again with the code. */
  const submit = async (credential: RegistrationResponseJSON, resent: boolean) => {
    let sendTheCode = false
    try {
      const { data, error, response } = await api.POST("/api/v1/auth/passkeys/registration/verify", {
        body: {
          credential,
          name: name.trim() || null,
          emailCode: codeNeeded ? emailCode : null,
        },
      })
      if (error || !data) throw withRetryAfter(error ?? new Error("Passkey registration failed"), response)
      onAdded(data.name)
    } catch (error) {
      const codes = getErrorCodes(error)
      if (codes.includes("Auth.ReauthenticationRequired")) {
        onReauthenticate()
        return
      }
      if (codes.includes("Passkey.EmailRequired")) {
        onEmailRequired()
        return
      }
      // T-B5: asked before anything was spent, so this answer can still bind once the code is in.
      // The code goes out at once; the field appears with it.
      if (codes.includes("TwoFactor.EmailCodeRequired") && !codeNeeded) {
        setHeld(credential)
        sendTheCode = true
        return
      }
      // A wrong or old code for the held answer: the code is typed again, or a new one asked for.
      // The answer stays held while its challenge lives.
      if (resent && codes.includes("TwoFactor.EmailCodeInvalid")) {
        setMessage(getErrorMessage(error))
        return
      }
      // These three come after the challenge was spent (T-C13 among the
      // causes): only fresh options can go on, so the dialog offers them. No
      // second ceremony on the options it holds, which could only be refused.
      if (codes.includes("Passkey.NameTooLong")) {
        setOver(true)
        setNameError(getErrorMessage(error))
      } else if (codes.includes("Passkey.LimitReached")) {
        setOver(true)
        setMessage(getErrorMessage(error))
      } else if (codes.includes("Passkey.VerificationFailed")) {
        // The person is signed in and adding a passkey: no sign-in advice.
        setOver(true)
        setMessage(`${getErrorMessage(error)} ${t("profile.passkeys.startAgain")}`)
      } else if (resent) {
        // Any other failure of the held answer: fresh options, never the same answer again.
        setOver(true)
        setMessage(`${passkeyFailureMessage(error) ?? ""} ${t("profile.passkeys.startAgain")}`.trim())
      } else {
        setMessage(passkeyFailureMessage(error))
      }
    } finally {
      busyRef.current = false
      setBusy(false)
      if (sendTheCode) void sendCode()
    }
  }

  const confirm = () => {
    if (!canConfirm || busyRef.current) return
    // The server checks the name after the ceremony, having spent its challenge:
    // said here, before the device asks for a fingerprint and keeps a passkey
    // the account would never hold. The server's limit is on code points.
    if ([...name.trim()].length > PASSKEY_NAME_MAX_CODE_POINTS) {
      setNameError(t("profile.passkeys.nameTooLong", { max: PASSKEY_NAME_MAX_CODE_POINTS }))
      return
    }
    // Options in their last seconds would only fail at the server: fresh ones. A held answer
    // goes no further than its challenge either (T-B5).
    if (
      registration.usableUntil !== null &&
      performance.now() > registration.usableUntil - OPTIONS_MARGIN_MS
    ) {
      onStartAgain()
      return
    }
    busyRef.current = true
    if (held) {
      setBusy(true)
      setMessage(null)
      setNameError(null)
      void submit(held, true)
      return
    }
    // Before anything is awaited (T-C2).
    const ceremony = registration.helper.startRegistration({ optionsJSON: registration.options })
    setBusy(true)
    setMessage(null)
    setNameError(null)
    void ceremony.then(
      (credential) => submit(credential, false),
      (error: unknown) => {
        busyRef.current = false
        setBusy(false)
        if (isCeremonyCancelled(error)) return
        // What the ceremony raised is the browser's, whatever its name: never the
        // server's unknown fault, and no sign-in advice to a person signed in.
        setMessage(
          isAlreadyOnThisDevice(error)
            ? t("profile.passkeys.alreadyOnThisDevice")
            : t("profile.passkeys.deviceRefused")
        )
      }
    )
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !busyRef.current) onClose()
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("profile.passkeys.addTitle")}</DialogTitle>
          <DialogDescription>{t("profile.passkeys.addBody")}</DialogDescription>
        </DialogHeader>
        <form
          id="add-passkey"
          onSubmit={(event) => {
            event.preventDefault()
            confirm()
          }}
        >
          <FieldGroup>
            <Field data-invalid={nameError ? true : undefined}>
              <FieldLabel htmlFor="new-passkey-name">{t("profile.passkeys.name")}</FieldLabel>
              <Input
                id="new-passkey-name"
                value={name}
                maxLength={128}
                placeholder={t("profile.passkeys.namePlaceholder")}
                aria-invalid={nameError ? true : undefined}
                onChange={(event) => setName(event.target.value)}
              />
              {nameError ? (
                <FieldError>{nameError}</FieldError>
              ) : (
                <FieldDescription>{t("profile.passkeys.nameHint")}</FieldDescription>
              )}
            </Field>
            {held ? (
              <p className="text-sm text-muted-foreground" data-testid="add-passkey-code-after-all">
                {t("profile.passkeys.codeAfterAll")}
              </p>
            ) : null}
            {codeNeeded ? (
              sentTo === null ? (
                <Field>
                  <FieldDescription>{t("profile.passkeys.emailCodeNeeded")}</FieldDescription>
                  {/* In a div: a vertical Field stretches its direct children. */}
                  <div>
                    <Button
                      type="button"
                      variant="secondary"
                      disabled={sending || over}
                      onClick={() => void sendCode()}
                    >
                      {sending ? <Spinner data-icon="inline-start" /> : null}
                      {t("auth.emailMeACode")}
                    </Button>
                  </div>
                </Field>
              ) : (
                <>
                  <OneTimeCodeField
                    id="new-passkey-email-code"
                    label={t("profile.passkeys.emailCode")}
                    value={emailCode}
                    onChange={setEmailCode}
                    disabled={busy || over}
                  >
                    <FieldDescription aria-live="polite">
                      {t("auth.emailCodeSentTo", { email: sentTo })}
                    </FieldDescription>
                  </OneTimeCodeField>
                  {/* A code that did not arrive, or that expired: a new one here. */}
                  {over ? null : (
                    <ResendCodeButton
                      availableAt={resendAt}
                      pending={sending}
                      onResend={() => void sendCode()}
                    />
                  )}
                </>
              )
            ) : null}
            {message ? (
              <Alert variant="destructive" data-testid="add-passkey-message">
                <AlertDescription>{message}</AlertDescription>
              </Alert>
            ) : null}
          </FieldGroup>
        </form>
        <DialogFooter>
          <Button type="button" variant="outline" disabled={busy} onClick={onClose}>
            {t("common.cancel")}
          </Button>
          {over ? (
            <Button type="button" onClick={onStartAgain}>
              {t("profile.passkeys.tryAgain")}
            </Button>
          ) : (
            <Button type="submit" form="add-passkey" disabled={!canConfirm}>
              {busy ? <Spinner data-icon="inline-start" /> : <KeyRound data-icon="inline-start" />}
              {t("profile.passkeys.create")}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
