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
 * class `ThemeProvider` will pick once the accounts app has captured the
 * hints (`flow-hints.ts`):
 *
 * 1. on a page of the flow kept for this tab (its address, without the two
 *    hints, matches the kept record's `marker`): the kept colour mode, or, when
 *    the user picked one since, the stored choice. Never the address, which the
 *    server fills with the application's mode on every page of the flow;
 * 2. otherwise, `ui_theme` in the address (an application's colour mode); an
 *    address that names a hint replaces the kept record, so that record is then
 *    not read;
 * 3. the colour mode kept for this tab (`auth.flow.hints`, unexpired);
 * 4. the stored choice (`ThemeProvider`'s `theme`);
 * 5. otherwise, or for `system`, the device's.
 *
 * Two arrivals end a kept record that this script cannot recognise as ending
 * it: a direct visit to an accounts page, and a new flow whose application
 * names no hint (`flow-hints.ts` decides both from the request's shape, which
 * this script does not read). There the kept mode is painted first, and
 * `ThemeProvider` settles it once React runs.
 *
 * Self-contained on purpose: it is serialized with `Function.prototype.toString`,
 * so its body may name nothing outside itself — no import, no module constant.
 * The tests hold its literals, and its digest, to the modules that write them.
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

export interface BootOptions {
  /**
   * Whether the address and the kept flow speak: true where the app captures
   * the hints (the accounts app). The console reads neither, as its
   * `ThemeProvider` reads neither.
   */
  hints: boolean
}

/* istanbul ignore next -- serialized into a file of its own, where coverage counters do not exist */
export function bootTheme(win: BootWindow, options: BootOptions = { hints: true }): void {
  const modes = ["light", "dark", "system"]
  const pick = (value: unknown): string | null =>
    typeof value === "string" && modes.includes(value) ? value : null
  // flow-hints.ts's digest, character for character.
  const digest = (value: string): string => {
    let hash = 0x811c9dc5
    for (let index = 0; index < value.length; index++) {
      hash ^= value.charCodeAt(index)
      hash = Math.imul(hash, 0x01000193)
    }
    return (hash >>> 0).toString(16)
  }

  let mode: string | null = null
  if (options.hints) {
    let params: URLSearchParams | null = null
    try {
      params = new URLSearchParams(win.location.search)
    } catch {
      // No readable address: only the kept record and the stored choice speak.
    }

    let kept: { theme?: unknown; marker?: unknown } | null = null
    try {
      const value = JSON.parse(win.sessionStorage.getItem("auth.flow.hints") ?? "null")
      if (value && typeof value.expiresAt === "number" && value.expiresAt > Date.now()) {
        kept = value
      }
    } catch {
      // Storage refused or damaged: no kept record.
    }

    let ownFlow = false
    let namesHints = false
    if (params) {
      namesHints = params.has("ui_locales") || params.has("ui_theme")
      if (kept && typeof kept.marker === "string") {
        const rest = new URLSearchParams(params)
        rest.delete("ui_locales")
        rest.delete("ui_theme")
        ownFlow = digest(rest.toString()) === kept.marker
      }
    }

    if (ownFlow) {
      mode = pick(kept?.theme)
    } else {
      mode = pick(params?.get("ui_theme"))
      if (!mode && !namesHints) {
        mode = pick(kept?.theme)
      }
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

/** The classic script `themeBoot()` serves: `bootTheme`, called on `window` with `options`. */
export function bootThemeScript(options: BootOptions): string {
  return `(${bootTheme.toString()})(window, ${JSON.stringify(options)});\n`
}
