import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import { Spinner } from "@authsystem/ui/spinner"

/**
 * "Resend code" action with the shared cooldown. Both account-deletion entry
 * points issue codes through the same per-address rate limit, so both throttle
 * the button identically — a user who taps twice gets a countdown, not a 429.
 */
export function ResendCodeButton({
  availableAt,
  pending,
  onResend,
}: {
  /** When the next resend becomes allowed; `null` means immediately. */
  availableAt: Date | null
  pending?: boolean
  onResend: () => void
}) {
  const { t } = useTranslation()
  const cooldown = useCountdown(availableAt)
  const ready = availableAt === null || cooldown.expired

  return (
    <Button
      type="button"
      variant="link"
      className="text-muted-foreground"
      disabled={!ready || pending}
      onClick={onResend}
    >
      {pending ? <Spinner /> : null}
      {ready
        ? t("accountDeletion.resendCode")
        : t("accountDeletion.resendIn", { seconds: cooldown.totalSeconds })}
    </Button>
  )
}
