/**
 * The colour mode, on `<html>` before the first paint.
 *
 * Each app's entry is a module script, and a module script runs after the
 * browser has painted the empty page. The page's background is light until
 * `<html>` carries `dark`, which `ThemeProvider` sets only once React runs.
 * Measured on the accounts build in a dark browser: every frame before the
 * first content was white, for a stored `dark`, for an application's
 * `ui_theme=dark`, and for `system` on a dark device, for as long as the
 * scripts took to arrive.
 *
 * `bootTheme` therefore runs as a classic script in `<head>`, which the browser
 * runs before it paints `<body>`: `themeBoot()` (`Auth_UI/theme-boot.ts`)
 * serializes it into a file of its own on the app's origin, so the CSP's
 * `script-src 'self'` admits it with no hash and no inline script. It picks the
 * class `ThemeProvider` will pick:
 *
 * 1. `ui_theme` in the address (an application's colour mode, `flow-hints.ts`);
 * 2. the colour mode kept for this tab's flow (`auth.flow.hints`, unexpired);
 * 3. the stored choice (`ThemeProvider`'s `theme`);
 * 4. otherwise, or for `system`, the device's.
 *
 * `ThemeProvider` takes over once React runs and settles the one case this
 * cannot see: a kept hint that the arriving page then ends (`flow-hints.ts`).
 *
 * Self-contained on purpose: it is serialized with `Function.prototype.toString`,
 * so its body may name nothing outside itself — no import, no module constant.
 * The tests hold its literals to the modules that write them.
 */

interface BootStorage {
  getItem(key: string): string | null
}

/** The parts of `window` the boot script reads; structural, so it needs no DOM types. */
export interface BootWindow {
  location: { search: string }
  sessionStorage: BootStorage
  localStorage: BootStorage
  matchMedia(query: string): { matches: boolean }
  document: { documentElement: { classList: { add(token: string): void } } }
}

/* istanbul ignore next -- serialized into a file of its own, where coverage counters do not exist */
export function bootTheme(win: BootWindow): void {
  const modes = ["light", "dark", "system"]
  const pick = (value: unknown): string | null =>
    typeof value === "string" && modes.includes(value) ? value : null

  let mode: string | null = null
  try {
    mode = pick(new URLSearchParams(win.location.search).get("ui_theme"))
  } catch {
    // No readable address: the next source decides.
  }

  if (!mode) {
    try {
      const kept = JSON.parse(win.sessionStorage.getItem("auth.flow.hints") ?? "null")
      if (kept && typeof kept.expiresAt === "number" && kept.expiresAt > Date.now()) {
        mode = pick(kept.theme)
      }
    } catch {
      // Storage refused or damaged: no kept hint.
    }
  }

  if (!mode) {
    try {
      mode = pick(win.localStorage.getItem("theme"))
    } catch {
      // Storage refused: no stored choice.
    }
  }

  if (!mode || mode === "system") {
    let dark = false
    try {
      dark = win.matchMedia("(prefers-color-scheme: dark)").matches
    } catch {
      // No media query support: light, as the stylesheet is.
    }
    mode = dark ? "dark" : "light"
  }

  win.document.documentElement.classList.add(mode)
}

/** The classic script `themeBoot()` serves: `bootTheme`, called on `window`. */
export function bootThemeScript(): string {
  return `(${bootTheme.toString()})(window);\n`
}
