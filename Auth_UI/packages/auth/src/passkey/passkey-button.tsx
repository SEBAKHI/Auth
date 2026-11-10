import { KeyRound, RotateCw } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { Spinner } from "@authsystem/ui/spinner"

import type { PasskeyCeremony } from "./use-passkey-sign-in"

/**
 * The passkey control of a page (S20 PR C, PK15): one button, and what went
 * wrong under it. Nothing at all while passkeys do not run here (the options
 * answered 404, or the browser has no WebAuthn), nor before the first answer
 * says whether they do, so a page renders exactly as before.
 *
 * Disabled until the options and the helper are in hand (T-C2). The message is
 * an alert, announced when it appears, and stays where the person is looking.
 */
export function PasskeyButton({
  ceremony,
  label,
  busyLabel,
  variant = "secondary",
  buttonRef,
}: {
  ceremony: PasskeyCeremony
  label: string
  busyLabel?: string
  /** `default` where the passkey is the way in the page offers first. */
  variant?: "default" | "secondary"
  buttonRef?: React.Ref<HTMLButtonElement>
}) {
  const { t } = useTranslation()
  if (ceremony.phase === "unavailable" || ceremony.phase === "checking") return null
  const busy = ceremony.phase === "busy"

  return (
    <div className="flex flex-col gap-3" data-testid="passkey-sign-in">
      <Button
        ref={buttonRef}
        type="button"
        variant={variant}
        className="w-full"
        disabled={ceremony.phase !== "ready"}
        onClick={ceremony.start}
      >
        {busy || ceremony.phase === "preparing" ? (
          <Spinner data-icon="inline-start" />
        ) : (
          <KeyRound data-icon="inline-start" />
        )}
        {busy ? (busyLabel ?? t("auth.passkey.waiting")) : label}
      </Button>
      {ceremony.message ? (
        <Alert variant="destructive" data-testid="passkey-message">
          <AlertDescription>
            <p>{ceremony.message}</p>
            {ceremony.phase === "failed" && ceremony.retryable ? (
              <Button type="button" variant="secondary" size="sm" onClick={ceremony.retry}>
                <RotateCw data-icon="inline-start" />
                {t("common.retry")}
              </Button>
            ) : null}
          </AlertDescription>
        </Alert>
      ) : null}
    </div>
  )
}
