import * as React from "react"
import { useTranslation } from "react-i18next"

import { auditActionI18nKey, auditActionTypeI18nKey } from "@/lib/audit-catalog"

/**
 * The one place an audit action or category is turned into words.
 *
 * The lookup itself is two lines, which is exactly why it had been written out
 * four separate times — in the table, in the detail dialog, in the filter list
 * and in the catalogue page — and why the two screens that never wrote it out
 * showed `user.deletion_requested` to a reader who had asked for Arabic.
 *
 * Both functions fall back to the stored code. A code this build has never
 * heard of — a row from before an action was retired, a server one release
 * ahead — is then shown as itself rather than as a missing-key marker, which is
 * still the truth about what happened.
 */
export function useAuditLabels() {
  const { t } = useTranslation()

  const actionLabel = React.useCallback(
    (code: string) =>
      t(`auditLogs.actions.${auditActionI18nKey(code)}`, {
        defaultValue: code,
      }),
    [t]
  )

  const actionTypeLabel = React.useCallback(
    (actionType: string) =>
      t(`auditLogs.actionTypes.${auditActionTypeI18nKey(actionType)}`, {
        defaultValue: actionType,
      }),
    [t]
  )

  return { actionLabel, actionTypeLabel }
}
