import * as React from "react"

/**
 * The parent's half of the isolated-frame protocol, shared by every frame that shows
 * markup the console or the sign-in pages did not write: the email preview and the
 * sign-in page designs.
 *
 * Such a frame is sandboxed without `allow-same-origin`, so its document has an opaque
 * origin: it reaches no storage, cookie or token of the page that frames it, and
 * `event.origin` of what it posts is the string "null". The receiver page inside it says
 * "ready" to its parent; this hook accepts that once, only from its own frame's window
 * and only with the opaque origin, and hands the window over. After that it either stops
 * listening (the email preview: one message, then silence, so a page the frame was
 * navigated to finds no listener), or keeps listening to that same window for the
 * frame's answers (a design says when it has rendered).
 *
 * Messages to the frame go with `"*"` as the target: an opaque origin cannot be named.
 * What keeps them where they belong is that they go to the window that has just proved
 * it is this frame, and carry nothing secret.
 */
export interface IsolatedFrameHandlers {
  /** The `type` of the frame's "ready" message. */
  ready: string
  /** Called once, with the frame's window, when it has said "ready". */
  onReady: (frame: Window) => void
  /**
   * The frame's later messages, from the same window and the opaque origin. Absent:
   * the hook stops listening at "ready".
   */
  onMessage?: (data: { type?: unknown } & Record<string, unknown>) => void
  /** How long the frame has to say "ready". */
  timeoutMs: number
  /** Called when it did not, after which nothing it says is heard. */
  onTimeout: () => void
}

export function useIsolatedFrame(
  frameRef: React.RefObject<HTMLIFrameElement | null>,
  handlers: IsolatedFrameHandlers,
  deps: React.DependencyList
) {
  // The latest handlers, read when a message arrives, so a re-render with new
  // callbacks neither re-runs the handshake nor calls a stale one.
  const latest = React.useRef(handlers)
  React.useLayoutEffect(() => {
    latest.current = handlers
  })

  // A layout effect: the listener is in place before the frame's document can
  // possibly have loaded and said "ready".
  React.useLayoutEffect(() => {
    const frame = frameRef.current
    if (!frame) return
    let readyWindow: Window | null = null
    const stop = () => {
      window.removeEventListener("message", receive)
      window.clearTimeout(timeout)
    }
    function receive(event: MessageEvent) {
      const target = frame?.contentWindow
      if (!target || event.source !== target || event.origin !== "null") return
      const data = event.data as ({ type?: unknown } & Record<string, unknown>) | null
      if (typeof data !== "object" || data === null) return
      if (readyWindow === null) {
        if (data.type !== latest.current.ready) return
        readyWindow = target
        window.clearTimeout(timeout)
        if (!latest.current.onMessage) window.removeEventListener("message", receive)
        latest.current.onReady(target)
        return
      }
      // A second "ready" is a document the frame was navigated to: it is not answered.
      if (data.type === latest.current.ready) return
      latest.current.onMessage?.(data)
    }
    const timeout = window.setTimeout(() => {
      stop()
      latest.current.onTimeout()
    }, handlers.timeoutMs)
    window.addEventListener("message", receive)
    return stop
    // The caller names what starts a new handshake (a new email, a new design).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
}
