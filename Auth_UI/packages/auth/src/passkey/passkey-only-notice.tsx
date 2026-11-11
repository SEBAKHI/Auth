import { useTranslation } from "react-i18next"

import { Alert, AlertDescription } from "@authsystem/ui/alert"

import { useAuth } from "../auth-context"

/**
 * What a first passkey costs an account that administers the platform and has no authenticator app
 * (REVIEW 160 F2, D-160-2 (b)): it becomes the account's only qualifying factor, and the password,
 * emailed-code and provider doors then refuse it (D-S20-4 (ii-s)), whatever the enforcement switch
 * says. One plain sentence above «Add a passkey», on the offer and on the set-up page, and nothing
 * for anyone else.
 *
 * Who administers the platform is read from the auth context: the account's platform permissions,
 * or the server withholding them until a second factor (`mfa_req`, which only a platform
 * administrator ever carries). For display only: the server holds the rule.
 */
export function PasskeyOnlyNotice({ totpEnabled }: { totpEnabled: boolean | null | undefined }) {
  const { t } = useTranslation()
  const { permissions, mfaRequirement } = useAuth()
  const administers = mfaRequirement !== "none" || permissions.length > 0
  if (!administers || totpEnabled === true) return null
  return (
    <Alert data-testid="passkey-only-notice">
      <AlertDescription>{t("auth.passkeyOffer.onlyWayIn")}</AlertDescription>
    </Alert>
  )
}
