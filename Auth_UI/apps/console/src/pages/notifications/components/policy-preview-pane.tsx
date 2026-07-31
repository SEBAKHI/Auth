import { Monitor, Smartphone } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  PolicyDocument,
  type PolicyDisclosure,
  type PrivacyPolicyContent,
} from "@astoom/ui/common/policy-document"
import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@astoom/ui/tooltip"

/**
 * Rendered-output pane for the policy editor, mirroring the notification
 * PreviewPane: mode tabs at the row start, device-width toggles at the row
 * end. The rendered mode uses the SAME PolicyDocument component the public
 * page uses, so the preview cannot drift from what users see; the JSON mode
 * exposes the stored document for anyone who needs the raw shape.
 */
export function PolicyPreviewPane({
  content,
  disclosure,
  dir,
  version,
}: {
  content: PrivacyPolicyContent
  disclosure: PolicyDisclosure
  dir: "ltr" | "rtl"
  version: string
}) {
  const { t } = useTranslation()
  const [mode, setMode] = React.useState<"preview" | "json">("preview")
  const [width, setWidth] = React.useState<"desktop" | "mobile">("desktop")

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Tabs
          value={mode}
          onValueChange={(value) => setMode(value as "preview" | "json")}
        >
          <TabsList>
            <TabsTrigger value="preview">
              {t("notifications.policyPreview")}
            </TabsTrigger>
            <TabsTrigger value="json">{t("notifications.policyJsonTab")}</TabsTrigger>
          </TabsList>
        </Tabs>
        <div className="flex items-center gap-1">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant={width === "desktop" ? "secondary" : "ghost"}
                size="icon-sm"
                aria-label={t("notifications.desktopWidth")}
                onClick={() => setWidth("desktop")}
              >
                <Monitor />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{t("notifications.desktopWidth")}</TooltipContent>
          </Tooltip>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant={width === "mobile" ? "secondary" : "ghost"}
                size="icon-sm"
                aria-label={t("notifications.mobileWidth")}
                onClick={() => setWidth("mobile")}
              >
                <Smartphone />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{t("notifications.mobileWidth")}</TooltipContent>
          </Tooltip>
        </div>
      </div>

      <div className="flex justify-center rounded-md border bg-muted/30 p-3">
        {mode === "preview" ? (
          <div
            // This block renders the policy in its *own* language, not the
            // console's, so it has to own its alignment too — that is the point
            // of the preview.
            // eslint-disable-next-line no-restricted-syntax
            dir={dir}
            className={
              width === "mobile"
                ? "max-h-[70vh] w-[375px] overflow-y-auto rounded-md border bg-background p-4"
                : "max-h-[70vh] w-full overflow-y-auto rounded-md border bg-background p-6"
            }
          >
            <div className="flex flex-col gap-6">
              <div className="flex flex-col items-center gap-2 text-center">
                <h1 className="text-2xl font-semibold tracking-tight">
                  {content.title}
                </h1>
                <div className="flex flex-wrap items-center justify-center gap-2">
                  <Badge variant="secondary">
                    {content.versionLabel} {version}
                  </Badge>
                  <span className="text-sm text-muted-foreground">
                    {content.effectiveDate}
                  </span>
                </div>
              </div>
              <PolicyDocument
                content={content}
                disclosure={disclosure}
                dir={dir}
              />
            </div>
          </div>
        ) : (
          <pre
            dir="ltr"
            className="max-h-[70vh] w-full overflow-auto whitespace-pre-wrap rounded-md border bg-background p-4 text-xs"
          >
            {JSON.stringify(content, null, 2)}
          </pre>
        )}
      </div>
    </div>
  )
}
