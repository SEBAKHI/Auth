import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { ReauthenticateDialog } from "@authsystem/auth/reauthenticate-dialog"
import { useSignInOptions } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"

import { removePasswordOffer } from "./remove-password-offer"

/**
 * «Remove password» (OI-215, D-215-3). Afterwards the account signs in with a
 * code sent to its email, and every other device is signed out. The removal
 * needs a sign-in that proved the password (and the authenticator app, when it
 * is on) within the re-authentication window: an older one is asked to sign in
 * again, and the "sign in again" dialog asks that sign-in for full proof, so a
 * sign-in by code there still asks for the password.
 *
 * Where the emailed code is the second step, or the account holds platform
 * authority without the authenticator app (D-110-1), the card says what to do
 * first instead of showing a button that would only be refused.
 */
export function RemovePasswordCard({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const signInOptions = useSignInOptions()
  const offer = removePasswordOffer(me, signInOptions?.codeSignInOffered === true)
  const [confirmOpen, setConfirmOpen] = React.useState(false)
  const [reauthenticate, setReauthenticate] = React.useState(false)

  const mutation = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/api/v1/Auth/remove-password")
      if (error) throw error
    },
    onSuccess: async () => {
      setConfirmOpen(false)
      toast.success(t("profile.passwordRemoved"))
      await queryClient.invalidateQueries({ queryKey: ["me"] })
    },
    onError: (error) => {
      setConfirmOpen(false)
      if (getErrorCodes(error).includes("Auth.ReauthenticationRequired")) {
        setReauthenticate(true)
        return
      }
      toast.error(getErrorMessage(error))
    },
  })

  if (offer === "none") return null

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{t("profile.removePassword")}</CardTitle>
        <CardDescription>
          {offer === "offered"
            ? t("profile.removePasswordBody")
            : offer === "blockedByEmailCode"
              ? t("profile.removePasswordBlockedByEmailCode")
              : t("profile.removePasswordBlockedByPlatformAuthority")}
        </CardDescription>
      </CardHeader>
      {offer === "offered" ? (
        <CardContent>
          <Button
            variant="outline"
            onClick={() => setConfirmOpen(true)}
            disabled={mutation.isPending}
          >
            {t("profile.removePassword")}
          </Button>
        </CardContent>
      ) : null}

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={(open) => {
          if (!mutation.isPending) setConfirmOpen(open)
        }}
        title={t("profile.removePasswordConfirmTitle")}
        description={t("profile.removePasswordConfirmBody", {
          email: me.email ?? "",
        })}
        confirmLabel={t("profile.removePassword")}
        destructive
        loading={mutation.isPending}
        onConfirm={() => {
          if (!mutation.isPending) mutation.mutate()
        }}
      />

      {/* Mounted only while needed, so closing it resets it. */}
      {reauthenticate ? (
        <ReauthenticateDialog
          open
          onOpenChange={(open) => !open && setReauthenticate(false)}
          description={t("profile.removePasswordReauthenticate")}
        />
      ) : null}
    </Card>
  )
}
