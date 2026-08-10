import { Moon, Sun } from "lucide-react"
import { useTranslation } from "react-i18next"

import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@authsystem/ui/tooltip"
import { cn } from "@authsystem/ui/utils"

/** Which colour scheme the preview simulates the recipient's client using. */
export type PreviewScheme = "light" | "dark"

/**
 * Light/dark switch for a rendered email preview.
 *
 * Shared by the authoring preview and the delivery-log inspector so an operator
 * investigating a complaint sees exactly what the author saw. Without it, the render
 * silently followed the console's own theme and two admins looking at one template
 * could see different results with no control explaining why.
 */
export function PreviewSchemeToggle({
  scheme,
  onSchemeChange,
}: {
  scheme: PreviewScheme
  onSchemeChange: (scheme: PreviewScheme) => void
}) {
  const { t } = useTranslation()

  return (
    <ToggleGroup
      type="single"
      spacing={0}
      variant="outline"
      size="sm"
      value={scheme}
      aria-label={t("notifications.previewScheme")}
      onValueChange={(next) => {
        // Radix emits "" when the active item is pressed again; a preview always
        // simulates one scheme or the other, so an empty value is ignored.
        if (next === "light" || next === "dark") onSchemeChange(next)
      }}
    >
      <Tooltip>
        <TooltipTrigger asChild>
          <ToggleGroupItem value="light" aria-label={t("notifications.previewLight")}>
            <Sun />
          </ToggleGroupItem>
        </TooltipTrigger>
        <TooltipContent>{t("notifications.previewLight")}</TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger asChild>
          <ToggleGroupItem value="dark" aria-label={t("notifications.previewDark")}>
            <Moon />
          </ToggleGroupItem>
        </TooltipTrigger>
        <TooltipContent>{t("notifications.previewDark")}</TooltipContent>
      </Tooltip>
    </ToggleGroup>
  )
}

/**
 * The sandboxed frame that renders a server-composed email (no scripts, no
 * same-origin).
 *
 * `colorScheme` on the embedding element sets the embedded document's preferred
 * colour scheme (CSS Color Adjust 1), which is what drives
 * `@media (prefers-color-scheme: dark)` inside the frame — an email's entire dark
 * palette lives behind that query, so this is the only way to see it without sending
 * a real message. It also paints the frame's own canvas, so a layout that leaves its
 * background transparent previews against the surface a real client would supply.
 * Never hardcode a backdrop here: that fabricates a light client and would validate a
 * dark-mode fix against a lie.
 */
export function EmailPreviewFrame({
  html,
  scheme,
  className,
  height,
}: {
  html: string
  scheme: PreviewScheme
  className?: string
  height?: string
}) {
  const { t } = useTranslation()

  return (
    <iframe
      title={t("notifications.preview")}
      sandbox=""
      srcDoc={html}
      className={cn("rounded-md border", className)}
      style={{ colorScheme: scheme, height }}
    />
  )
}
