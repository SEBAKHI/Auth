import { useTranslation } from "react-i18next"

import type { Schemas } from "@authsystem/api/types"
import { Badge } from "@authsystem/ui/badge"
import { formatDateTime } from "@authsystem/ui/format"

type WebhookKey = Schemas["WebhookKeyDto"]

/**
 * A webhook key's status cell: active or revoked, and, for a key stored under an HMAC
 * key that no longer issues (S11), the re-issue badge with the date it stops working
 * unused. Its next use re-fingerprints it; rotating it stays the way to act now.
 */
export function WebhookKeyStatus({ webhookKey }: { webhookKey: WebhookKey }) {
  const { t } = useTranslation()

  return (
    <div className="flex flex-col items-start gap-1">
      <Badge variant={webhookKey.isRevoked ? "destructive" : "default"}>
        {webhookKey.isRevoked ? t("common.revoked") : t("common.active")}
      </Badge>
      {webhookKey.needsReissue ? (
        <>
          <Badge variant="outline">{t("webhookKeys.needsReissue")}</Badge>
          <span className="max-w-56 text-xs text-muted-foreground">
            {webhookKey.reissueBy
              ? t("webhookKeys.needsReissueBy", {
                  date: formatDateTime(webhookKey.reissueBy),
                })
              : t("webhookKeys.needsReissueNow")}
          </span>
        </>
      ) : null}
    </div>
  )
}
