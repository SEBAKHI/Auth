import { Monitor, Smartphone } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import { Skeleton } from "@astoom/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@astoom/ui/tooltip"
import type { NotificationPreviewDto } from "../lib"

/**
 * Shared rendered-output pane used by both the template and layout previews:
 * HTML/plain-text mode tabs, desktop/mobile width toggle, and the sandboxed
 * iframe (no scripts, no same-origin) that displays the server-rendered result.
 * The toolbar arrangement is fixed (mode tabs at the row start, device toggles
 * at the row end) so every page reads identically, and logical flex properties
 * mirror it automatically under RTL.
 */
export function PreviewPane({
  preview,
  error,
  frameHeight = "540px",
}: {
  preview: NotificationPreviewDto | null
  error?: string | null
  frameHeight?: string
}) {
  const { t } = useTranslation()
  const [mode, setMode] = React.useState<"html" | "text">("html")
  const [width, setWidth] = React.useState<"desktop" | "mobile">("desktop")

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Tabs value={mode} onValueChange={(value) => setMode(value as "html" | "text")}>
          <TabsList>
            <TabsTrigger value="html">{t("notifications.htmlTab")}</TabsTrigger>
            <TabsTrigger value="text">{t("notifications.textTab")}</TabsTrigger>
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

      {error ? (
        <p className="text-sm text-destructive" role="alert">
          {error}
        </p>
      ) : null}

      <div className="flex justify-center rounded-md border bg-muted/30 p-3">
        {preview ? (
          mode === "html" ? (
            <iframe
              title={t("notifications.preview")}
              sandbox=""
              srcDoc={preview.html ?? ""}
              className={
                width === "mobile"
                  ? "w-[375px] rounded-md border bg-white"
                  : "w-full rounded-md border bg-white"
              }
              style={{ height: frameHeight }}
            />
          ) : (
            <pre
              dir="auto"
              className="w-full overflow-auto whitespace-pre-wrap rounded-md border bg-background p-4 text-sm"
              style={{ height: frameHeight }}
            >
              {preview.text}
            </pre>
          )
        ) : (
          <Skeleton className="w-full" style={{ height: frameHeight }} />
        )}
      </div>
    </div>
  )
}
