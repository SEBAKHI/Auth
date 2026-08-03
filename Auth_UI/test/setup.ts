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

// jsdom implements no media queries at all, and both the theme provider and
// the mobile-viewport hook call matchMedia on mount. The shim answers width
// queries from `window.innerWidth` so a test can render at phone size by
// setting it; anything else (prefers-color-scheme, …) simply does not match.
if (typeof window.matchMedia !== "function") {
  const maxWidthQuery = /\(max-width:\s*(\d+)px\)/
  window.matchMedia = ((query: string) => {
    const limit = maxWidthQuery.exec(query)
    return {
      media: query,
      matches: limit ? window.innerWidth <= Number(limit[1]) : false,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }
  }) as typeof window.matchMedia
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
