import { useTranslation } from "react-i18next"

import { Badge } from "@authsystem/ui/badge"

/**
 * An outcome has three states, and the third is not a blank.
 *
 * `AuditLogs.IsSuccess` is nullable because rows written before the column
 * existed had their outcome invented on the way out — the read path returned
 * true for every one of them, so the screen showed a clean history because it
 * had been told to, not because it was one. Rendering a null as anything other
 * than "not recorded" carries that same claim forward on exactly the rows it was
 * never true of.
 *
 * Its own module rather than an export from either of the two that use it: the
 * columns and the detail dialog show the same three states, and the table that
 * renders the columns is also the thing that opens the dialog — so putting the
 * badge in either one would make that chain fold back on itself.
 */
export function ResultBadge({ value }: { value?: boolean | null }) {
  const { t } = useTranslation()

  if (value === true) {
    return <Badge variant="secondary">{t("auditLogs.success")}</Badge>
  }
  if (value === false) {
    return <Badge variant="destructive">{t("auditLogs.failure")}</Badge>
  }
  return <Badge variant="outline">{t("auditLogs.notRecorded")}</Badge>
}
