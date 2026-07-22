import "@testing-library/jest-dom/vitest"

// jsdom lacks ResizeObserver, which Radix primitives (ScrollArea, etc.) use.
if (!("ResizeObserver" in globalThis)) {
  class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  globalThis.ResizeObserver =
    ResizeObserver as unknown as typeof globalThis.ResizeObserver
}

// jsdom does not implement the pointer-capture and scrolling APIs used by
// Radix Select. No-op shims keep interaction tests aligned with browser APIs.
if (typeof Element.prototype.hasPointerCapture !== "function") {
  Object.defineProperty(Element.prototype, "hasPointerCapture", {
    value: () => false,
  })
}
if (typeof Element.prototype.setPointerCapture !== "function") {
  Object.defineProperty(Element.prototype, "setPointerCapture", {
    value: () => undefined,
  })
}
if (typeof Element.prototype.releasePointerCapture !== "function") {
  Object.defineProperty(Element.prototype, "releasePointerCapture", {
    value: () => undefined,
  })
}
if (typeof Element.prototype.scrollIntoView !== "function") {
  Object.defineProperty(Element.prototype, "scrollIntoView", {
    value: () => undefined,
  })
}
