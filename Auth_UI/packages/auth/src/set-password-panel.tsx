import { MailCheck } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { Button } from "@authsystem/ui/button"
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import { Spinner } from "@authsystem/ui/spinner"

/**
 * Long enough that an impatient double-click cannot cost the user a sign-in.
 * /forgot-password shares the "login" rate-limit bucket with real sign-ins, so a
 * user who taps this a few times can 429 themselves out of the very thing they
 * are trying to reach. The proper per-user server-side cap is a separate change;
 * this keeps the obvious self-inflicted case from happening at all.
 */
const RESEND_COOLDOWN_SECONDS = 60

interface SetPasswordPanelProps {
  /** The signed-in user's own address; the link goes here and nowhere else. */
  email: string
}

interface SentState {
  maskedEmail: string
  expiresAt: Date | null
}

/**
 * Lets an account that has never had a password acquire one.
 *
 * It sends the ordinary reset link rather than taking a new password inline, and
 * that is the security decision, not a shortcut. A session alone must not be
 * enough to plant a first password: an attacker holding a stolen session would
 * otherwise gain a credential that outlives revoking it. This system has no
 * change-email capability anywhere, so the mailbox is the one factor a
 * session-only attacker genuinely cannot reach.
 *
 * The capability itself is not new - /forgot-password already grants it to
 * anyone with the mailbox, and ResetPasswordCommandHandler already tolerates a
 * null hash. What was missing was any way to find it from inside the product.
 *
 * Rendered as a bare body so callers can frame it: a Card on Profile > Security,
 * the AuthLayout on the forced-change page.
 */
export function SetPasswordPanel({ email }: SetPasswordPanelProps) {
  const { t } = useTranslation()
  const [pending, setPending] = React.useState(false)
  const [sent, setSent] = React.useState<SentState | null>(null)
  const [cooldownUntil, setCooldownUntil] = React.useState<Date | null>(null)
  const cooldown = useCountdown(cooldownUntil)
  const expiry = useCountdown(sent?.expiresAt ?? null)

  const cooling = cooldownUntil !== null && !cooldown.expired

  const send = async () => {
    setPending(true)
    try {
      const { data, error } = await api.POST("/api/v1/Auth/forgot-password", {
        body: { email },
      })
      if (error) throw error
      setSent({
        // The response is deliberately identical for unknown addresses, so the
        // masked value is what we echo back rather than the address we sent.
        maskedEmail: data?.maskedEmail ?? email,
        expiresAt: data?.expiresAt ? new Date(data.expiresAt) : null,
      })
      setCooldownUntil(new Date(Date.now() + RESEND_COOLDOWN_SECONDS * 1000))
    } catch (error) {
      // Includes the server's own 429 message, which is the one case where the
      // user needs to be told to wait rather than shown a generic failure.
      toast.error(getErrorMessage(error))
    } finally {
      setPending(false)
    }
  }

  if (sent) {
    return (
      <div className="flex flex-col gap-4">
        <div className="flex items-start gap-3">
          <MailCheck
            className="size-5 shrink-0 text-muted-foreground"
            aria-hidden
          />
          <p className="text-sm text-muted-foreground">
            {t("auth.resetLinkSentDescription", { email: sent.maskedEmail })}
          </p>
        </div>

        {sent.expiresAt ? (
          <p className="text-sm text-muted-foreground tabular-nums">
            {expiry.expired
              ? t("auth.resetLinkExpired")
              : t("auth.resetLinkExpiresIn", { time: expiry.label })}
          </p>
        ) : null}

        <Button
          type="button"
          variant="outline"
          className="w-fit"
          disabled={pending || cooling}
          onClick={() => void send()}
        >
          {pending ? <Spinner /> : null}
          {cooling
            ? t("auth.resendAvailableIn", { time: cooldown.label })
            : t("auth.resendResetLink")}
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">
        {t("profile.setPasswordBody")}
      </p>
      <Button
        type="button"
        className="w-fit"
        disabled={pending}
        onClick={() => void send()}
      >
        {pending ? <Spinner /> : null}
        {t("profile.emailSetPasswordLink")}
      </Button>
    </div>
  )
}
