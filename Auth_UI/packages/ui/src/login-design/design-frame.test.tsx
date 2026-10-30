import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"

import { act, render } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"

import { applyThemeCss } from "@authsystem/ui/theme/apply-theme"
import {
  DESIGN_FRAME_SANDBOX,
  DESIGN_FRAME_SRC,
  DESIGN_MESSAGES,
  DESIGN_RENDER_TIMEOUT_MS,
  DesignFrame,
  type DesignFrameState,
} from "./design-frame"
import { DESIGN_TOKENS } from "./design-tokens"

/**
 * The parent's half of the design frame protocol: the design goes to the frame once,
 * after a "ready" from that frame's own window with the opaque origin; a new mode or
 * appearance is sent to the same window without the design; the frame is invisible until
 * it says "rendered", and gone when it fails.
 *
 * Deliberate breaks: add "allow-same-origin" to the sandbox; accept a "ready" from any
 * origin; resend the design on a mode change; show the frame before "rendered".
 */

const READY = { type: DESIGN_MESSAGES.ready }
// Read from disk: a stylesheet imported in a test is handled as CSS, not text.
const presetCss = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "..", "preset.css"), "utf8")

function frameOf() {
  return document.querySelector("iframe")!
}

function stubFrameWindow(frame: HTMLIFrameElement) {
  const target = { postMessage: vi.fn() }
  Object.defineProperty(frame, "contentWindow", { configurable: true, get: () => target })
  return target
}

function say(data: unknown, source: unknown, origin = "null") {
  act(() => {
    window.dispatchEvent(new MessageEvent("message", { data, origin, source: source as MessageEventSource }))
  })
}

function renderFrame(props: Partial<React.ComponentProps<typeof DesignFrame>> = {}) {
  const states: DesignFrameState[] = []
  const view = render(
    <DesignFrame
      html="<p>design</p>"
      lang="ar"
      dir="rtl"
      mode="light"
      title="Sign-in page design"
      onStateChange={(state) => states.push(state)}
      {...props}
    />
  )
  return { ...view, states }
}

afterEach(() => {
  vi.useRealTimers()
})

describe("DesignFrame", () => {
  it("is sandboxed with exactly allow-scripts allow-popups allow-popups-to-escape-sandbox", () => {
    expect(DESIGN_FRAME_SANDBOX).toBe("allow-scripts allow-popups allow-popups-to-escape-sandbox")
    renderFrame()
    const frame = frameOf()
    expect(frame.getAttribute("sandbox")).toBe("allow-scripts allow-popups allow-popups-to-escape-sandbox")
    for (const forbidden of ["allow-same-origin", "allow-forms", "allow-top-navigation"]) {
      expect(frame.getAttribute("sandbox")).not.toContain(forbidden)
    }
    expect(frame.getAttribute("src")).toBe(DESIGN_FRAME_SRC)
    expect(frame.style.colorScheme).toBe("light")
    expect([...frame.attributes].map((attribute) => attribute.name)).not.toContain(["src", "doc"].join(""))
  })

  it("is invisible until the frame says it has rendered", () => {
    const { states } = renderFrame()
    const target = stubFrameWindow(frameOf())
    expect(frameOf().className).toContain("invisible")

    say(READY, target)
    expect(frameOf().className).toContain("invisible")
    say({ type: DESIGN_MESSAGES.rendered }, target)

    expect(frameOf().className).not.toContain("invisible")
    expect(states).toEqual(["pending", "rendered"])
  })

  it("sends the design once, to its own frame's window, with the tokens and the mode", () => {
    document.documentElement.style.setProperty("--primary", "oklch(0.5 0.1 250)")
    renderFrame({ mode: "dark" })
    const target = stubFrameWindow(frameOf())

    say(READY, { postMessage: vi.fn() })
    say(READY, target, window.location.origin)
    say({ type: "something-else" }, target)
    expect(target.postMessage).not.toHaveBeenCalled()

    say(READY, target)
    say(READY, target)
    expect(target.postMessage).toHaveBeenCalledTimes(1)
    const [message, targetOrigin] = target.postMessage.mock.calls[0]
    expect(targetOrigin).toBe("*")
    expect(message).toMatchObject({
      type: DESIGN_MESSAGES.render,
      html: "<p>design</p>",
      lang: "ar",
      dir: "rtl",
      mode: "dark",
    })
    expect(message.tokens["--primary"]).toBe("oklch(0.5 0.1 250)")
    document.documentElement.style.removeProperty("--primary")
  })

  it("repaints the same frame on a new mode or appearance, without sending the design again", () => {
    const { rerender } = renderFrame()
    const frame = frameOf()
    const target = stubFrameWindow(frame)
    say(READY, target)
    target.postMessage.mockClear()

    rerender(<DesignFrame html="<p>design</p>" lang="ar" dir="rtl" mode="dark" title="t" />)
    expect(frameOf()).toBe(frame)
    expect(target.postMessage).toHaveBeenCalledTimes(1)
    expect(target.postMessage.mock.calls[0][0]).toMatchObject({ type: DESIGN_MESSAGES.theme, mode: "dark" })
    expect(target.postMessage.mock.calls[0][0]).not.toHaveProperty("html")

    act(() => applyThemeCss(null))
    expect(target.postMessage).toHaveBeenCalledTimes(2)
    expect(target.postMessage.mock.calls[1][0]).toMatchObject({ type: DESIGN_MESSAGES.theme })
  })

  it("a new design is a new frame", () => {
    const { rerender } = renderFrame()
    const first = frameOf()
    rerender(<DesignFrame html="<p>other</p>" lang="ar" dir="rtl" mode="light" title="t" />)
    expect(frameOf()).not.toBe(first)
  })

  it("is removed when the frame fails, or says nothing in time: the plain panel stays", () => {
    const failed = renderFrame()
    const target = stubFrameWindow(frameOf())
    say(READY, target)
    say({ type: DESIGN_MESSAGES.failed }, target)
    expect(document.querySelector("iframe")).toBeNull()
    expect(failed.states.at(-1)).toBe("failed")
    failed.unmount()

    vi.useFakeTimers()
    const silent = renderFrame()
    act(() => {
      vi.advanceTimersByTime(DESIGN_RENDER_TIMEOUT_MS)
    })
    expect(document.querySelector("iframe")).toBeNull()
    expect(silent.states.at(-1)).toBe("failed")
  })

  it("loads while the design is on its way, and gets it in the same frame on arrival", () => {
    const { rerender } = renderFrame({ html: null })
    const frame = frameOf()
    const target = stubFrameWindow(frame)
    say(READY, target)
    expect(target.postMessage).not.toHaveBeenCalled()

    rerender(<DesignFrame html="<p>design</p>" lang="ar" dir="rtl" mode="dark" title="t" />)

    expect(frameOf()).toBe(frame)
    expect(target.postMessage).toHaveBeenCalledTimes(1)
    expect(target.postMessage.mock.calls[0][0]).toMatchObject({
      type: DESIGN_MESSAGES.render,
      html: "<p>design</p>",
      lang: "ar",
      dir: "rtl",
      mode: "dark",
    })
  })

  it("gives the render its five seconds from the send, not from the mount", () => {
    vi.useFakeTimers()
    const { rerender, states } = renderFrame({ html: null })
    const target = stubFrameWindow(frameOf())
    say(READY, target)

    // A slow server: the design is still being rendered long after the frame was ready.
    act(() => {
      vi.advanceTimersByTime(DESIGN_RENDER_TIMEOUT_MS * 2)
    })
    expect(document.querySelector("iframe")).not.toBeNull()
    expect(states.at(-1)).toBe("pending")

    rerender(
      <DesignFrame
        html="<p>design</p>"
        lang="ar"
        dir="rtl"
        mode="light"
        title="t"
        onStateChange={(state) => states.push(state)}
      />
    )
    act(() => {
      vi.advanceTimersByTime(DESIGN_RENDER_TIMEOUT_MS - 1)
    })
    expect(document.querySelector("iframe")).not.toBeNull()
    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(document.querySelector("iframe")).toBeNull()
    expect(states.at(-1)).toBe("failed")
  })
})

describe("DESIGN_TOKENS", () => {
  it("names only variables preset.css defines, so a renamed token cannot silently stop reaching designs", () => {
    expect(DESIGN_TOKENS.length).toBeGreaterThan(20)
    for (const token of DESIGN_TOKENS) expect(presetCss, token).toMatch(new RegExp(`\\n\\s*${token}:`))
  })
})
