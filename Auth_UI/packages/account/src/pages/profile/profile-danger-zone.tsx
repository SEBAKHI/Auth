import { useMutation } from "@tanstack/react-query"
import { Loader2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { useAuth } from "@astoom/auth/auth-context"
import { Button } from "@astoom/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { Field, FieldGroup, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@astoom/ui/input-otp"
import type { Schemas } from "@astoom/api/types"

const CODE_LENGTH = 6
/** Display-only mirror of the server's grace period (AccountDeletionSettings). */
const GRACE_DAYS = 30

/**
 * Self-service account deletion: re-authentication (password, or an emailed
 * code for external-only accounts) followed by a type-your-email confirmation.
 * On success the server revokes every credential, so the flow clears local
 * state and lands on the signed-out "deletion scheduled" screen.
 */
export function ProfileDangerZone({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const { logout } = useAuth()
  const navigate = useNavigate()

  const [reauthOpen, setReauthOpen] = React.useState(false)
  const [mode, setMode] = React.useState<"password" | "otp">("password")
  const [password, setPassword] = React.useState("")
  const [otpCode, setOtpCode] = React.useState("")
  const [confirmOpen, setConfirmOpen] = React.useState(false)
  const [confirmEmail, setConfirmEmail] = React.useState("")

  const resetFlow = React.useCallback(() => {
    setMode("password")
    setPassword("")
    setOtpCode("")
    setConfirmEmail("")
  }, [])

  const sendCodeMutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/v1/Users/me/deletion/send-code", {})
      if (error) throw error
    },
    onSuccess: () => {
      setMode("otp")
      toast.success(t("accountDeletion.codeSent"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const deleteMutation = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/api/v1/Users/me/deletion", {
        body: mode === "password" ? { password } : { otpCode },
      })
      if (error || !data) throw error ?? new Error("Request failed")
      return data
    },
    onSuccess: async (data) => {
      setConfirmOpen(false)
      // Navigate before clearing auth state so the RequireAuth guard around the
      // profile never gets a chance to bounce us to /login first.
      navigate("/deletion-scheduled", {
        replace: true,
        state: { graceEndsAtUtc: data.graceEndsAtUtc },
      })
      await logout()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const reauthComplete =
    mode === "password" ? password.length > 0 : otpCode.length === CODE_LENGTH

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
          {mode === "password" ? (
            <FieldGroup>
              <Field>
                <FieldLabel htmlFor="deletion-password">
                  {t("accountDeletion.reauthPasswordLabel")}
                </FieldLabel>
                <Input
                  id="deletion-password"
                  type="password"
                  autoComplete="current-password"
                  autoFocus
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                />
              </Field>
              <Button
                type="button"
                variant="link"
                className="w-fit px-0 text-muted-foreground"
                disabled={sendCodeMutation.isPending}
                onClick={() => sendCodeMutation.mutate()}
              >
                {sendCodeMutation.isPending ? (
                  <Loader2 className="animate-spin" />
                ) : null}
                {t("accountDeletion.noPasswordHint")}
              </Button>
            </FieldGroup>
          ) : (
            <div className="flex flex-col items-center gap-3">
              <InputOTP
                dir="ltr"
                maxLength={CODE_LENGTH}
                pattern={REGEXP_ONLY_DIGITS}
                value={otpCode}
                onChange={setOtpCode}
                autoFocus
                aria-label={t("accountDeletion.verificationCode")}
              >
                <InputOTPGroup>
                  {Array.from({ length: CODE_LENGTH }).map((_, index) => (
                    <InputOTPSlot key={index} index={index} />
                  ))}
                </InputOTPGroup>
              </InputOTP>
              <Button
                type="button"
                variant="link"
                className="text-muted-foreground"
                onClick={() => setMode("password")}
              >
                {t("accountDeletion.usePasswordHint")}
              </Button>
            </div>
          )}
          <DialogFooter>
            <Button
              variant="destructive"
              disabled={!reauthComplete}
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
        <Input
          aria-label={t("auth.email")}
          type="email"
          autoComplete="off"
          placeholder={me.email ?? ""}
          value={confirmEmail}
          onChange={(event) => setConfirmEmail(event.target.value)}
        />
      </ConfirmDialog>
    </>
  )
}
