import { act, render, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { initI18n } from "@authsystem/i18n"

import { EmailPreviewFrame, PREVIEW_READY_TIMEOUT_MS } from "./email-preview-frame"

/**
 * The preview protocol (card S02, targets 5, 7, 8, 9): the email goes to the
 * frame once, after a "ready" from that frame's own window with the opaque
 * origin, and never to anyone else; a frame that stays silent turns into an
 * alert pointing at the Text tab.
 */

const READY = { type: "authsystem.email-preview.ready" }

function frameOf() {
  return document.querySelector("iframe")!
}

/** Puts a spy where the frame's window is, and returns it. */
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

beforeEach(async () => {
  await initI18n()
})

afterEach(() => {
  vi.useRealTimers()
})

describe("EmailPreviewFrame", () => {
  it("frames /email-preview.html with sandbox=allow-scripts and nothing inline", () => {
    render(<EmailPreviewFrame html="<p>hi</p>" scheme="dark" height="300px" />)
    const frame = frameOf()
    expect(frame.getAttribute("src")).toBe("/email-preview.html")
    expect(frame.getAttribute("sandbox")).toBe("allow-scripts")
    expect([...frame.attributes].map((attribute) => attribute.name)).not.toContain(["src", "doc"].join(""))
    expect(frame.style.colorScheme).toBe("dark")
    expect(frame.style.height).toBe("300px")
  })

  it("sends nothing before a ready from its own frame, then the HTML once, to that window", () => {
    render(<EmailPreviewFrame html="<p>hi</p>" scheme="light" />)
    const target = stubFrameWindow(frameOf())
    expect(target.postMessage).not.toHaveBeenCalled()

    say(READY, target)
    expect(target.postMessage).toHaveBeenCalledTimes(1)
    expect(target.postMessage).toHaveBeenCalledWith({ type: "authsystem.email-preview.html", html: "<p>hi</p>" }, "*")
  })

  it("ignores a ready from another window, from a non-opaque origin, or of another type", () => {
    render(<EmailPreviewFrame html="<p>hi</p>" scheme="light" />)
    const target = stubFrameWindow(frameOf())
    say(READY, { postMessage: vi.fn() })
    say(READY, target, window.location.origin)
    say({ type: "something-else" }, target)
    say("authsystem.email-preview.ready", target)
    expect(target.postMessage).not.toHaveBeenCalled()
  })

  it("answers only the first ready: a second one sends nothing", () => {
    render(<EmailPreviewFrame html="<p>hi</p>" scheme="light" />)
    const target = stubFrameWindow(frameOf())
    say(READY, target)
    say(READY, target)
    expect(target.postMessage).toHaveBeenCalledTimes(1)
  })

  it("a new email is a new frame, which is sent that email once", () => {
    const { rerender } = render(<EmailPreviewFrame html="<p>one</p>" scheme="light" />)
    const first = frameOf()
    rerender(<EmailPreviewFrame html="<p>two</p>" scheme="light" />)
    const second = frameOf()
    expect(second).not.toBe(first)
    const target = stubFrameWindow(second)
    say(READY, target)
    expect(target.postMessage).toHaveBeenCalledWith({ type: "authsystem.email-preview.html", html: "<p>two</p>" }, "*")
    expect(target.postMessage).toHaveBeenCalledTimes(1)
  })

  it(`without a ready within ${PREVIEW_READY_TIMEOUT_MS / 1000} s shows the alert pointing at the Text tab, and stops listening`, () => {
    vi.useFakeTimers()
    render(<EmailPreviewFrame html="<p>hi</p>" scheme="light" />)
    const target = stubFrameWindow(frameOf())
    act(() => {
      vi.advanceTimersByTime(PREVIEW_READY_TIMEOUT_MS - 1)
    })
    expect(screen.queryByRole("alert")).toBeNull()
    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(screen.getByRole("alert")).toHaveTextContent(
      "The preview could not be shown here. Open the Text tab to read this message."
    )
    expect(document.querySelector("iframe")).toBeNull()
    say(READY, target)
    expect(target.postMessage).not.toHaveBeenCalled()
  })

  it("unmounting removes its own listener and its timer", () => {
    vi.useFakeTimers()
    const add = vi.spyOn(window, "addEventListener")
    const remove = vi.spyOn(window, "removeEventListener")
    const { unmount } = render(<EmailPreviewFrame html="<p>hi</p>" scheme="light" />)
    const added = add.mock.calls.filter(([type]) => type === "message").map(([, listener]) => listener)
    expect(added.length).toBeGreaterThan(0)
    expect(vi.getTimerCount()).toBeGreaterThan(0)
    unmount()
    const removed = remove.mock.calls.filter(([type]) => type === "message").map(([, listener]) => listener)
    // Every listener this component added is gone, not merely "some message listener".
    for (const listener of added) expect(removed).toContain(listener)
    expect(vi.getTimerCount()).toBe(0)
  })
})
