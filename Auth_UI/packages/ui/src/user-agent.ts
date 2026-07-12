/**
 * Minimal user-agent classifier for the sessions view: turns a raw UA string
 * into a human-readable browser/OS pair and a device class for the icon.
 * Heuristic by design — unknown agents fall back to "unknown"/desktop.
 */
export type DeviceType = "desktop" | "mobile" | "tablet"

export interface ParsedUserAgent {
  browser: string | null
  os: string | null
  deviceType: DeviceType
}

// Order matters: brands that embed other tokens (Edg before Chrome, Chrome
// before Safari, OPR before Chrome) are checked first.
const BROWSERS: Array<[RegExp, string]> = [
  [/\bEdg(?:e|A|iOS)?\//, "Microsoft Edge"],
  [/\bOPR\/|\bOpera\b/, "Opera"],
  [/\bSamsungBrowser\//, "Samsung Internet"],
  [/\bFirefox\/|\bFxiOS\//, "Firefox"],
  [/(?:\b|Headless)Chrome\/|\bCriOS\//, "Chrome"],
  [/\bSafari\//, "Safari"],
]

const OSES: Array<[RegExp, string]> = [
  [/Windows NT/, "Windows"],
  [/Android/, "Android"],
  [/iPhone|iPad|iPod/, "iOS"],
  [/Mac OS X|Macintosh/, "macOS"],
  [/CrOS/, "ChromeOS"],
  [/Linux/, "Linux"],
]

export function parseUserAgent(
  userAgent: string | null | undefined
): ParsedUserAgent {
  if (!userAgent) {
    return { browser: null, os: null, deviceType: "desktop" }
  }

  const browser = BROWSERS.find(([re]) => re.test(userAgent))?.[1] ?? null
  const os = OSES.find(([re]) => re.test(userAgent))?.[1] ?? null

  const isTablet = /\biPad\b|\bTablet\b|Android(?!.*Mobile)/.test(userAgent)
  const isMobile = /\bMobi|iPhone|iPod|Android.*Mobile/.test(userAgent)
  const deviceType: DeviceType = isTablet
    ? "tablet"
    : isMobile
      ? "mobile"
      : "desktop"

  return { browser, os, deviceType }
}
