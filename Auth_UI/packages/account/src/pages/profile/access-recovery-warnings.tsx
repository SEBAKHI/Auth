import { useTranslation } from "react-i18next"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import type { Schemas } from "@authsystem/api/types"

import { useTwoFactorStatus } from "./two-factor-status"

/**
 * What stands between an administrator and no way back in (owner, 2026-11-24: an
 * administrator could sign in on one device only). Two warnings, each from the
 * server's own reading:
 *
 * - **One device only:** the account signs in with its passkey alone (D-S20-4
 *   (ii-s)), and its one passkey is not synced, so it lives on one device. Losing
 *   that device ends every sign-in. A synced passkey, or a second one, answers it.
 * - **Nobody to restore it:** no other administrator could reset this account's
 *   second factor, by the reset's own rules.
 *
 * Nothing shows for an account that holds no platform authority: the server answers
 * neither question for it, and the status is not read.
 */
export function AccessRecoveryWarnings({
  me,
  passkeys,
}: {
  me: Schemas["UserDto"]
  /** The passkey list; null while passkeys do not run here, undefined while it loads. */
  passkeys: Schemas["PasskeyListDto"] | null | undefined
}) {
  const { t } = useTranslation()
  const status = useTwoFactorStatus(me, me.totpEnabled ?? Boolean(me.twoFactorEnabled))

  const onlyPasskey = passkeys?.passkeys.length === 1 ? passkeys.passkeys[0] : null
  const oneDevice =
    status?.signsInWithPasskeyOnly === true && onlyPasskey !== null && !onlyPasskey.isBackedUp
  const nobodyToRestore = status?.anotherAdministratorCanReset === false

  if (!oneDevice && !nobodyToRestore) return null

  return (
    <div className="flex flex-col gap-3">
      {oneDevice ? (
        <Alert data-testid="passkey-one-device-warning">
          <AlertTitle>{t("profile.passkeyOneDeviceTitle")}</AlertTitle>
          <AlertDescription>{t("profile.passkeyOneDevice")}</AlertDescription>
        </Alert>
      ) : null}
      {nobodyToRestore ? (
        <Alert data-testid="sole-administrator-warning">
          <AlertTitle>{t("profile.soleAdministratorTitle")}</AlertTitle>
          <AlertDescription>{t("profile.soleAdministrator")}</AlertDescription>
        </Alert>
      ) : null}
    </div>
  )
}
