import { Moon, Sun } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Alert, AlertDescription } from "@authsystem/ui/alert"
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

/** The two messages of the preview protocol (public/email-preview.html). */
const READY_MESSAGE = "authsystem.email-preview.ready"
const HTML_MESSAGE = "authsystem.email-preview.html"

/** How long the frame has to say "ready" before the preview is declared unavailable. */
export const PREVIEW_READY_TIMEOUT_MS = 5_000

/**
 * The frame that renders a server-composed email.
 *
 * The email is not handed to the frame as an inline document: such a frame
 * inherits the console's CSP, and an email needs inline styles the console's
 * policy refuses (card S02). It goes to /email-preview.html, a
 * static file with a policy of its own (web.config, <location>): no network
 * but images, no script but that file's own receiver, inline styles allowed.
 * `sandbox="allow-scripts"` - and nothing else - gives the frame an opaque
 * origin, so the receiver can run while the email reaches no console storage,
 * token or cookie; the email's own scripts and event handlers are refused by
 * the frame's script-src.
 *
 * The protocol: the frame says "ready" to its parent; this component accepts
 * that once, only from its own frame's window and only with the opaque
 * origin ("null"), and answers with the HTML, once. A frame that never says
 * ready (its script blocked, the file missing) shows an alert pointing to the
 * Text tab instead of an empty box.
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
export function EmailPreviewFrame(props: {
  html: string
  scheme: PreviewScheme
  className?: string
  height?: string
}) {
  // A new email is a new frame: each mounted frame is sent exactly one email.
  return <PreviewDocument key={useGeneration(props.html)} {...props} />
}

/** A number that grows each time `value` changes (state derived during render). */
function useGeneration(value: string) {
  const [state, setState] = React.useState({ value, generation: 0 })
  if (state.value !== value) {
    const next = { value, generation: state.generation + 1 }
    setState(next)
    return next.generation
  }
  return state.generation
}

function PreviewDocument({
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
  const frameRef = React.useRef<HTMLIFrameElement>(null)
  const [unavailable, setUnavailable] = React.useState(false)

  // A layout effect: the listener is in place before the frame's document can
  // possibly have loaded and said "ready".
  React.useLayoutEffect(() => {
    const frame = frameRef.current
    if (!frame) return
    const stop = () => {
      window.removeEventListener("message", receive)
      window.clearTimeout(timeout)
    }
    function receive(event: MessageEvent) {
      const target = frame?.contentWindow
      if (!target || event.source !== target || event.origin !== "null") return
      if ((event.data as { type?: unknown } | null)?.type !== READY_MESSAGE) return
      stop()
      // "*" because the frame's origin is opaque, and an opaque origin cannot
      // be named as a target. What keeps the email where it belongs is that it
      // is sent once, to the window that has just proved it is this frame, and
      // never again: a later "ready" - from a page the frame was navigated to -
      // finds no listener.
      target.postMessage({ type: HTML_MESSAGE, html }, "*")
    }
    const timeout = window.setTimeout(() => {
      stop()
      setUnavailable(true)
    }, PREVIEW_READY_TIMEOUT_MS)
    window.addEventListener("message", receive)
    return stop
  }, [html])

  if (unavailable) {
    return (
      <Alert className={className}>
        <AlertDescription>
          {t("notifications.previewUnavailable", { tab: t("notifications.textTab") })}
        </AlertDescription>
      </Alert>
    )
  }

  return (
    <iframe
      ref={frameRef}
      title={t("notifications.preview")}
      src="/email-preview.html"
      sandbox="allow-scripts"
      className={cn("rounded-md border", className)}
      style={{ colorScheme: scheme, height }}
    />
  )
}
