import * as React from "react"

import { useIsolatedFrame } from "@authsystem/ui/isolated-frame/use-isolated-frame"
import { readDesignTokens } from "@authsystem/ui/login-design/design-tokens"
import { THEME_APPLIED_EVENT } from "@authsystem/ui/theme/apply-theme"
import { platformFontFaces } from "@authsystem/ui/theme/platform-fonts"
import { cn } from "@authsystem/ui/utils"

/**
 * The sandbox of a design's frame, exactly: scripts for the receiver (the frame's policy
 * runs no other), and pop-ups that leave the sandbox so a link opens a normal new tab.
 * Never `allow-same-origin` (the design would share the page's origin, storage and
 * tokens), `allow-forms` or `allow-top-navigation` (it could take the page away from the
 * visitor). `design-frame.test.tsx` pins the value.
 */
export const DESIGN_FRAME_SANDBOX = "allow-scripts allow-popups allow-popups-to-escape-sandbox"

/** The receiver page each app serves beside its index.html, with a policy of its own. */
export const DESIGN_FRAME_SRC = "/login-design-frame.html"

/** The messages of the design protocol (login-design-frame.html). */
export const DESIGN_MESSAGES = {
  ready: "authsystem.login-design.ready",
  render: "authsystem.login-design.render",
  theme: "authsystem.login-design.theme",
  rendered: "authsystem.login-design.rendered",
  failed: "authsystem.login-design.failed",
} as const

/** How long the frame has to say it has rendered before the plain panel is kept. */
export const DESIGN_RENDER_TIMEOUT_MS = 5_000

export type DesignFrameState = "pending" | "rendered" | "failed"
export type DesignMode = "light" | "dark"

export interface DesignFrameProps {
  /**
   * The rendered design, as the server returned it; null while it is on its way. The
   * frame is mounted meanwhile and loads its own page, and the design goes to it as
   * soon as both are there: one after the other, they cost a sign-in page two waits.
   */
  html: string | null
  /** The design's language and direction; read with the design, not before it. */
  lang: string
  dir: "ltr" | "rtl"
  /** The mode the design is shown in; it sets the frame's colour scheme. */
  mode: DesignMode
  /**
   * The element whose appearance the design follows. The page's root on a sign-in page;
   * a scoped preview element in the console, which shows an application's appearance
   * without recolouring itself.
   */
  tokensFrom?: () => Element | null
  /**
   * A value that changes whenever the appearance under `tokensFrom` does, beyond what the
   * page announces itself (the console's scoped preview).
   */
  appearanceKey?: string
  title: string
  className?: string
  onStateChange?: (state: DesignFrameState) => void
}

/**
 * One design in its isolated frame.
 *
 * The frame is invisible until the receiver says it has rendered: the surface behind it
 * stays as it was, and any failure (the receiver blocked, an exception, nothing within
 * five seconds) leaves it that way. A new design is a new frame; a new mode or appearance
 * is sent to the same frame, which repaints in place.
 */
export function DesignFrame(props: DesignFrameProps) {
  // A new design (or language) is a new frame, sent that design once. The first design
  // into a frame that was waiting for one is not new: that frame was mounted for it.
  const identity = props.html === null ? null : `${props.lang}|${props.dir}|${props.html}`
  return <DesignDocument key={useGeneration(identity)} {...props} />
}

/**
 * A number that grows each time `value` changes (state derived during render), except
 * from null: a frame waiting for its first value keeps its number when the value comes.
 */
function useGeneration(value: string | null) {
  const [state, setState] = React.useState({ value, generation: 0 })
  if (state.value !== value) {
    const next = { value, generation: state.value === null ? state.generation : state.generation + 1 }
    setState(next)
    return next.generation
  }
  return state.generation
}

/**
 * Grows when the page's appearance may have changed: the platform's (or an application's)
 * stylesheet was replaced, or the root's class or style changed (the mode).
 */
function usePageAppearanceVersion() {
  const [version, setVersion] = React.useState(0)
  React.useEffect(() => {
    const bump = () => setVersion((current) => current + 1)
    document.addEventListener(THEME_APPLIED_EVENT, bump)
    const observer = new MutationObserver(bump)
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class", "style"] })
    return () => {
      document.removeEventListener(THEME_APPLIED_EVENT, bump)
      observer.disconnect()
    }
  }, [])
  return version
}

function DesignDocument({
  html,
  lang,
  dir,
  mode,
  tokensFrom,
  appearanceKey,
  title,
  className,
  onStateChange,
}: DesignFrameProps) {
  const frameRef = React.useRef<HTMLIFrameElement>(null)
  const windowRef = React.useRef<Window | null>(null)
  const [state, setState] = React.useState<DesignFrameState>("pending")
  // Whether the design has gone to the frame: the render's own wait starts there.
  const sentRef = React.useRef(false)
  const [sent, setSent] = React.useState(false)
  const pageVersion = usePageAppearanceVersion()

  const report = React.useRef(onStateChange)
  React.useLayoutEffect(() => {
    report.current = onStateChange
  })
  React.useEffect(() => {
    report.current?.(state)
  }, [state])

  const tokens = React.useCallback(
    () => readDesignTokens(tokensFrom?.() ?? document.documentElement),
    [tokensFrom]
  )

  // Read when the design is sent: the frame may say "ready" before the design arrives,
  // or after, and either way it gets the latest mode and appearance with it.
  const latest = React.useRef({ html, lang, dir, mode, tokens })
  React.useLayoutEffect(() => {
    latest.current = { html, lang, dir, mode, tokens }
  })
  const send = React.useCallback(() => {
    const frame = windowRef.current
    const design = latest.current
    if (!frame || design.html === null || sentRef.current) return
    sentRef.current = true
    frame.postMessage(
      {
        type: DESIGN_MESSAGES.render,
        html: design.html,
        lang: design.lang,
        dir: design.dir,
        mode: design.mode,
        tokens: design.tokens(),
        // The platform's font files: the frame sees none of the page's, and registers these.
        fonts: platformFontFaces(),
      },
      "*"
    )
    setSent(true)
  }, [])

  useIsolatedFrame(
    frameRef,
    {
      ready: DESIGN_MESSAGES.ready,
      timeoutMs: DESIGN_RENDER_TIMEOUT_MS,
      onTimeout: () => setState("failed"),
      onReady: (frame) => {
        windowRef.current = frame
        send()
      },
      onMessage: (data) => {
        if (data.type === DESIGN_MESSAGES.rendered) setState((current) => (current === "pending" ? "rendered" : current))
        else if (data.type === DESIGN_MESSAGES.failed) setState("failed")
      },
    },
    []
  )

  // Sent but never rendered (the receiver threw before it could say so) is a failure too.
  // Counted from the send, not the mount: the frame may wait on the server's render.
  React.useEffect(() => {
    if (state !== "pending" || !sent) return
    const timer = window.setTimeout(() => setState("failed"), DESIGN_RENDER_TIMEOUT_MS)
    return () => window.clearTimeout(timer)
  }, [state, sent])

  // A new mode or appearance repaints the same document: the design is not sent again.
  // Before the design has gone, there is nothing to repaint: it goes with the latest.
  const firstPaint = React.useRef(true)
  React.useEffect(() => {
    if (firstPaint.current) {
      firstPaint.current = false
      return
    }
    if (!sentRef.current) return
    windowRef.current?.postMessage(
      { type: DESIGN_MESSAGES.theme, mode, tokens: tokens(), fonts: platformFontFaces() },
      "*"
    )
  }, [mode, pageVersion, appearanceKey, tokens])

  // The design that arrives after the frame said "ready". After the repaint above, so a
  // design and a new mode in the same render go as the design alone, in that mode.
  React.useEffect(() => {
    send()
  }, [html, send])

  if (state === "failed") return null

  return (
    <iframe
      ref={frameRef}
      src={DESIGN_FRAME_SRC}
      sandbox={DESIGN_FRAME_SANDBOX}
      title={title}
      data-state={state}
      className={cn("block size-full border-0", state !== "rendered" && "invisible", className)}
      style={{ colorScheme: mode }}
    />
  )
}
