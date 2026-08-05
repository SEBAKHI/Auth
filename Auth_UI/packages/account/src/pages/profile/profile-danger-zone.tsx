import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { useAuth } from "@authsystem/auth/auth-context"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import {
  OTP_CODE_LENGTH,
  OtpInput,
  RESEND_COOLDOWN_MS,
} from "@authsystem/ui/common/otp-input"
import { ResendCodeButton } from "@authsystem/ui/common/resend-code-button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { Input } from "@authsystem/ui/input"
import type { Schemas } from "@authsystem/api/types"
import { Spinner } from "@authsystem/ui/spinner"

/** Display-only mirror of the server's grace period (AccountDeletionSettings). */
const GRACE_DAYS = 30

/**
 * Self-service account deletion: re-authentication followed by a
 * type-your-email confirmation. The re-auth factor is possession of the
 * account's mailbox — an emailed code, for every account — which is the same
 * factor the public "delete my account" wizard uses. The password is never
 * asked for: external-only (Google/Apple) accounts have none, so a password
 * prompt would strand exactly the users least able to answer it. On success the
 * server revokes every credential, so the flow clears local state and lands on
 * the signed-out "deletion scheduled" screen.
 */
export function ProfileDangerZone({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const { logout } = useAuth()
  const navigate = useNavigate()

  const [reauthOpen, setReauthOpen] = React.useState(false)
  const [codeSent, setCodeSent] = React.useState(false)
  const [otpCode, setOtpCode] = React.useState("")
  const [cooldownUntil, setCooldownUntil] = React.useState<Date | null>(null)
  const [confirmOpen, setConfirmOpen] = React.useState(false)
  const [confirmEmail, setConfirmEmail] = React.useState("")

  const resetFlow = React.useCallback(() => {
    setCodeSent(false)
    setOtpCode("")
    setCooldownUntil(null)
    setConfirmEmail("")
  }, [])

  const sendCodeMutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/v1/Users/me/deletion/send-code", {})
      if (error) throw error
    },
    onSuccess: () => {
      setCodeSent(true)
      setOtpCode("")
      setCooldownUntil(new Date(Date.now() + RESEND_COOLDOWN_MS))
      toast.success(t("accountDeletion.codeSent"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const deleteMutation = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/api/v1/Users/me/deletion", {
        body: { otpCode },
      })
      if (error || !data) throw error ?? new Error("Request failed")
      return data
    },
    onSuccess: async (data) => {
      setConfirmOpen(false)
      // flushSync commits the route swap BEFORE logout flips auth state —
      // otherwise the still-mounted RequireAuth guard around the profile can
      // observe "unauthenticated" first and win the race with a /login
      // redirect, losing the grace deadline carried in navigation state.
      navigate("/deletion-scheduled", {
        replace: true,
        state: { graceEndsAtUtc: data.graceEndsAtUtc },
        flushSync: true,
      })
      await logout()
    },
    onError: (error) => {
      // A verified code is single-use, and a rejected one must not strand the
      // user inside the confirmation dialog: step back to the re-auth dialog
      // with an empty field so they can retype or request a fresh code.
      setOtpCode("")
      setConfirmOpen(false)
      setReauthOpen(true)
      toast.error(getErrorMessage(error))
    },
  })

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle>{t("accountDeletion.dangerZone")}</CardTitle>
          <CardDescription>
            {t("accountDeletion.dangerZoneSubtitle")}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col items-start gap-4">
          <p className="text-sm text-muted-foreground">
            {t("accountDeletion.deleteWarning", { days: GRACE_DAYS })}
          </p>
          <Button
            variant="destructive"
            onClick={() => {
              resetFlow()
              setReauthOpen(true)
            }}
          >
            {t("accountDeletion.deleteAccount")}
          </Button>
        </CardContent>
      </Card>

      <Dialog
        open={reauthOpen}
        onOpenChange={(open) => {
          setReauthOpen(open)
          if (!open && !confirmOpen) resetFlow()
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("accountDeletion.reauthTitle")}</DialogTitle>
            <DialogDescription>
              {t("accountDeletion.reauthSubtitle")}
            </DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-4">
            <p className="text-sm text-muted-foreground">
              {t("accountDeletion.codeHint", { email: me.email ?? "" })}
            </p>
            {codeSent ? (
              <div className="flex flex-col items-center gap-3">
                <OtpInput
                  value={otpCode}
                  onChange={setOtpCode}
                  label={t("accountDeletion.verificationCode")}
                  autoFocus
                />
                <ResendCodeButton
                  availableAt={cooldownUntil}
                  pending={sendCodeMutation.isPending}
                  onResend={() => sendCodeMutation.mutate()}
                />
              </div>
            ) : (
              <Button
                type="button"
                variant="outline"
                disabled={sendCodeMutation.isPending}
                onClick={() => sendCodeMutation.mutate()}
              >
                {sendCodeMutation.isPending ? <Spinner /> : null}
                {t("accountDeletion.sendCode")}
              </Button>
            )}
          </div>
          <DialogFooter>
            <Button
              variant="destructive"
              disabled={otpCode.length !== OTP_CODE_LENGTH}
              onClick={() => {
                setReauthOpen(false)
                setConfirmOpen(true)
              }}
            >
              {t("accountDeletion.continue")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={(open) => {
          setConfirmOpen(open)
          if (!open) resetFlow()
        }}
        title={t("accountDeletion.confirmTitle")}
        description={t("accountDeletion.confirmBody")}
        confirmLabel={t("accountDeletion.scheduleDeletion")}
        destructive
        loading={deleteMutation.isPending}
        confirmDisabled={
          confirmEmail.trim().toLowerCase() !==
          (me.email ?? "").trim().toLowerCase()
        }
        onConfirm={() => deleteMutation.mutate()}
      >
        {/* Compared character-for-character against the account address, so it
            is pinned LTR like every other email field. */}
        <Input
          aria-label={t("auth.email")}
          type="email"
          dir="ltr"
          autoComplete="off"
          placeholder={me.email ?? ""}
          value={confirmEmail}
          onChange={(event) => setConfirmEmail(event.target.value)}
        />
      </ConfirmDialog>
    </>
  )
}
