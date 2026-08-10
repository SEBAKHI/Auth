import { Monitor, Smartphone } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { directionForLanguage } from "@authsystem/i18n"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@authsystem/ui/tooltip"
import type { NotificationPreviewDto } from "../lib"
import {
  EmailPreviewFrame,
  PreviewSchemeToggle,
  type PreviewScheme,
} from "./email-preview-frame"

/**
 * Shared rendered-output pane used by both the template and layout previews:
 * HTML/plain-text mode tabs, a light/dark scheme toggle, a desktop/mobile width
 * toggle, and the sandboxed iframe (no scripts, no same-origin) that displays the
 * server-rendered result. The toolbar arrangement is fixed (mode tabs at the row
 * start, simulation toggles at the row end) so every page reads identically, and
 * logical flex properties mirror it automatically under RTL.
 *
 * The scheme toggle is not a cosmetic nicety: an email's dark palette lives behind
 * `@media (prefers-color-scheme: dark)`, so without it an author cannot see half of
 * what they are shipping, and a dark-mode regression is only discoverable by sending
 * a real message to a real device. `color-scheme` on the embedding element sets the
 * embedded document's preferred scheme (CSS Color Adjust 1), which drives the media
 * query inside the frame regardless of the console's own theme — the previous
 * behaviour, where the render silently followed whatever theme the admin happened to
 * be using, was not reproducible between two people looking at the same template.
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
  const [scheme, setScheme] = React.useState<PreviewScheme>("light")

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Tabs value={mode} onValueChange={(value) => setMode(value as "html" | "text")}>
          <TabsList>
            <TabsTrigger value="html">{t("notifications.htmlTab")}</TabsTrigger>
            <TabsTrigger value="text">{t("notifications.textTab")}</TabsTrigger>
          </TabsList>
        </Tabs>
        <div className="flex items-center gap-2">
          <PreviewSchemeToggle scheme={scheme} onSchemeChange={setScheme} />
          <ToggleGroup
            type="single"
            spacing={0}
            variant="outline"
            size="sm"
            value={width}
            aria-label={t("notifications.previewWidth")}
            onValueChange={(next) => {
              if (next === "desktop" || next === "mobile") setWidth(next)
            }}
          >
            <Tooltip>
              <TooltipTrigger asChild>
                <ToggleGroupItem
                  value="desktop"
                  aria-label={t("notifications.desktopWidth")}
                >
                  <Monitor />
                </ToggleGroupItem>
              </TooltipTrigger>
              <TooltipContent>{t("notifications.desktopWidth")}</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <ToggleGroupItem
                  value="mobile"
                  aria-label={t("notifications.mobileWidth")}
                >
                  <Smartphone />
                </ToggleGroupItem>
              </TooltipTrigger>
              <TooltipContent>{t("notifications.mobileWidth")}</TooltipContent>
            </Tooltip>
          </ToggleGroup>
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
            <EmailPreviewFrame
              html={preview.html ?? ""}
              scheme={scheme}
              className={width === "mobile" ? "w-[375px]" : "w-full"}
              height={frameHeight}
            />
          ) : (
            <pre
              // The preview renders one specific locale's copy, and the DTO says
              // which — so bind it rather than guess. `auto` reads the value, so
              // an empty or Latin-leading body rendered `ltr` for an Arabic send.
              dir={directionForLanguage(preview.languageCode ?? "")}
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
