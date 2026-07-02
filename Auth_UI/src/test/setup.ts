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
