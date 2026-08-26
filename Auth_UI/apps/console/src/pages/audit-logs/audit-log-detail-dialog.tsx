import { useTranslation } from "react-i18next"

import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { formatDateTime } from "@authsystem/ui/format"
import type { Schemas } from "@authsystem/api/types"

import { useAuditLabels } from "./audit-log-labels"
import { ResultBadge } from "./result-badge"

function Row({ label, value }: { label: string; value?: string | null }) {
  if (!value) return null
  return (
    <div className="grid grid-cols-3 gap-2 py-1.5 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      {/* Every value here is of unknown or opposite direction — entity types and
          ids, IP addresses, user agents, application names. Isolating once in the
          helper keeps each one intact inside an RTL page, instead of letting the
          bidi algorithm move trailing punctuation and separators to the far edge. */}
      <dd className="col-span-2 break-words">
        <bdi dir="auto">{value}</bdi>
      </dd>
    </div>
  )
}

export function AuditLogDetailDialog({
  open,
  onOpenChange,
  log,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  log: Schemas["AuditLogDto"]
}) {
  const { t } = useTranslation()
  const { actionLabel, actionTypeLabel } = useAuditLabels()
  const action = log.action ?? ""

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          {/* The name a reader can read; the code it is stored under is a row
              below, because that is the string they need to quote elsewhere. */}
          <DialogTitle>{actionLabel(action)}</DialogTitle>
        </DialogHeader>

        <dl className="divide-y">
          <Row label={t("auditLogs.actionCode")} value={action} />
          <Row
            label={t("auditLogs.actionType")}
            value={log.actionType ? actionTypeLabel(log.actionType) : null}
          />
          {/* The outcome, always rendered — including when it was never recorded.
              A row that simply omits the result reads as "it went fine". */}
          <div className="grid grid-cols-3 gap-2 py-1.5 text-sm">
            <dt className="text-muted-foreground">{t("auditLogs.result")}</dt>
            <dd className="col-span-2">
              <ResultBadge value={log.isSuccess} />
            </dd>
          </div>
          <Row label={t("auditLogs.errorMessage")} value={log.errorMessage} />
          {/* Two people, two rows. Folding them into one under the "actor"
              heading is how an account an administrator locked was listed as
              having locked itself. */}
          <Row
            label={t("auditLogs.actor")}
            value={log.performedByEmail ?? log.performedByName}
          />
          <Row
            label={t("auditLogs.subject")}
            value={log.userEmail ?? log.userName}
          />
          <Row label={t("common.application")} value={log.applicationName} />
          <Row
            label={t("auditLogs.target")}
            value={
              log.entityType
                ? `${log.entityType}${log.entityId ? ` · ${log.entityId}` : ""}`
                : null
            }
          />
          <Row label={t("auditLogs.ipAddress")} value={log.ipAddress} />
          <Row label="User agent" value={log.userAgent} />
          <Row
            label={t("auditLogs.timestamp")}
            value={formatDateTime(log.timestamp)}
          />
        </dl>

        {log.oldValues ? (
          <div className="flex flex-col gap-1">
            <p className="text-sm font-medium">{t("auditLogs.oldValues")}</p>
            <pre className="max-h-40 overflow-auto rounded-lg border bg-muted p-3 text-xs">
              {log.oldValues}
            </pre>
          </div>
        ) : null}
        {log.newValues ? (
          <div className="flex flex-col gap-1">
            <p className="text-sm font-medium">{t("auditLogs.newValues")}</p>
            <pre className="max-h-40 overflow-auto rounded-lg border bg-muted p-3 text-xs">
              {log.newValues}
            </pre>
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}
