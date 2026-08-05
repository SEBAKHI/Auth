import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { Button } from "@authsystem/ui/button"
import { Spinner } from "@authsystem/ui/spinner"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { OTP_CODE_LENGTH, OtpInput } from "@authsystem/ui/common/otp-input"
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"

/**
 * Email-verification OTP dialog. Auto-requests a code when opened, shows a
 * live mm:ss expiry countdown, and verifies against the backend token store.
 * Identify the target user by `userId` (admin flows) or `email` alone
 * (anonymous flows such as the login page).
 */
export function VerifyEmailDialog({
  open,
  onOpenChange,
  email,
  userId,
  onVerified,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  email: string
  userId?: string
  onVerified?: () => void
}) {
  const { t } = useTranslation()
  const [otp, setOtp] = React.useState("")
  const [expiresAt, setExpiresAt] = React.useState<Date | null>(null)
  const [maskedEmail, setMaskedEmail] = React.useState<string | null>(null)
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null)
  const countdown = useCountdown(expiresAt)

  const handleOpenChange = (next: boolean) => {
    if (!next) {
      setOtp("")
      setErrorMessage(null)
    }
    onOpenChange(next)
  }

  const finishVerified = () => {
    toast.success(t("auth.emailVerifiedSuccess"))
    handleOpenChange(false)
    onVerified?.()
  }

  const resendMutation = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST(
        "/api/v1/Auth/resend-verification-email",
        { body: { email } }
      )
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      setOtp("")
      setErrorMessage(null)
      setExpiresAt(data?.expiresAt ? new Date(data.expiresAt) : null)
      setMaskedEmail(data?.maskedEmail ?? null)
    },
    onError: (error) => {
      if (
        getErrorCodes(error).includes("EmailVerification.EmailAlreadyVerified")
      ) {
        finishVerified()
        return
      }
      setErrorMessage(getErrorMessage(error))
    },
  })

  const verifyMutation = useMutation({
    mutationFn: async (code: string) => {
      const { error } = await api.POST("/api/v1/Auth/verify-email", {
        body: userId ? { userId, otp: code } : { email, otp: code },
      })
      if (error) throw error
    },
    onSuccess: finishVerified,
    onError: (error) => {
      if (
        getErrorCodes(error).includes("EmailVerification.EmailAlreadyVerified")
      ) {
        finishVerified()
        return
      }
      setErrorMessage(getErrorMessage(error))
    },
  })

  // Auto-request a code when the dialog opens, but never burn a still-valid
  // one (each resend invalidates all previously issued codes server-side).
  const { mutate: requestCode } = resendMutation
  React.useEffect(() => {
    if (!open) return
    if (!expiresAt || expiresAt.getTime() <= Date.now()) requestCode()
  }, [open, expiresAt, requestCode])

  const canVerify =
    otp.length === OTP_CODE_LENGTH &&
    Boolean(expiresAt) &&
    !countdown.expired &&
    !verifyMutation.isPending

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      {/* A single OTP field — the default width would leave it stranded. */}
      <DialogContent size="md">
        <DialogHeader className="items-center px-8 text-center">
          <DialogTitle>{t("auth.verifyEmailTitle")}</DialogTitle>
          <DialogDescription>
            {t("auth.verifyEmailDescription", {
              email: maskedEmail ?? email,
            })}
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col items-center gap-4 py-2">
          <OtpInput
            value={otp}
            onChange={(value) => {
              setOtp(value)
              setErrorMessage(null)
            }}
            onComplete={(code: string) => {
              if (!countdown.expired) verifyMutation.mutate(code)
            }}
            label={t("auth.verifyEmailCodeLabel")}
            disabled={
              !expiresAt || countdown.expired || verifyMutation.isPending
            }
          />

          {expiresAt ? (
            countdown.expired ? (
              <p className="text-sm text-destructive">{t("auth.codeExpired")}</p>
            ) : (
              <p className="text-sm text-muted-foreground tabular-nums">
                {t("auth.codeExpiresIn", { time: countdown.label })}
              </p>
            )
          ) : resendMutation.isPending ? (
            <Spinner className="text-muted-foreground" />
          ) : null}

          {errorMessage ? (
            <p className="text-center text-sm text-destructive">
              {errorMessage}
            </p>
          ) : null}
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            disabled={resendMutation.isPending}
            onClick={() => resendMutation.mutate()}
          >
            {resendMutation.isPending ? (
              <Spinner />
            ) : null}
            {t("auth.resendCode")}
          </Button>
          <Button
            disabled={!canVerify}
            onClick={() => verifyMutation.mutate(otp)}
          >
            {verifyMutation.isPending ? (
              <Spinner />
            ) : null}
            {t("auth.verify")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
