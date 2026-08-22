import "@testing-library/jest-dom/vitest"

// Node can expose an experimental `localStorage` accessor that resolves to
// undefined unless the process was started with --localstorage-file. That
// accessor shadows jsdom's working implementation and makes storage-dependent
// components vary with the Node invocation. Install the browser contract the
// tests need when that accessor is present.
const localStorageDescriptor = Object.getOwnPropertyDescriptor(
  globalThis,
  "localStorage"
)
if (!localStorageDescriptor || localStorageDescriptor.get) {
  class MemoryStorage implements Storage {
    readonly #values = new Map<string, string>()

    get length() {
      return this.#values.size
    }

    clear() {
      this.#values.clear()
    }

    getItem(key: string) {
      return this.#values.get(String(key)) ?? null
    }

    key(index: number) {
      return [...this.#values.keys()][index] ?? null
    }

    removeItem(key: string) {
      this.#values.delete(String(key))
    }

    setItem(key: string, value: string) {
      this.#values.set(String(key), String(value))
    }
  }

  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: new MemoryStorage(),
  })
}

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
