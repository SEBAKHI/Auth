import { useTranslation } from "react-i18next"

import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { formatDateTime } from "@astoom/ui/format"
import type { Schemas } from "@astoom/api/types"

function Row({ label, value }: { label: string; value?: string | null }) {
  if (!value) return null
  return (
    <div className="grid grid-cols-3 gap-2 py-1.5 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="col-span-2 break-words">{value}</dd>
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

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90svh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{log.action}</DialogTitle>
        </DialogHeader>

        <dl className="divide-y">
          <Row
            label={t("auditLogs.actor")}
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
          <div className="space-y-1">
            <p className="text-sm font-medium">{t("auditLogs.oldValues")}</p>
            <pre className="max-h-40 overflow-auto rounded-lg border bg-muted p-3 text-xs">
              {log.oldValues}
            </pre>
          </div>
        ) : null}
        {log.newValues ? (
          <div className="space-y-1">
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
