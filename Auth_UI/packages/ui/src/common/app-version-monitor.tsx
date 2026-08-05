import * as React from "react"

import {
  clearCacheBustParameter,
  currentModuleEntry,
  detectAvailableAppUpdate,
  reloadWithCacheBust,
} from "./app-version"

const DEFAULT_CHECK_INTERVAL_MS = 60_000
const UPDATE_TARGET_STORAGE_KEY = "auth.ui.update-target"

function readAttemptedTarget(): string | null {
  try {
    return window.sessionStorage.getItem(UPDATE_TARGET_STORAGE_KEY)
  } catch {
    return null
  }
}

function writeAttemptedTarget(target: string | null): void {
  try {
    if (target) {
      window.sessionStorage.setItem(UPDATE_TARGET_STORAGE_KEY, target)
    } else {
      window.sessionStorage.removeItem(UPDATE_TARGET_STORAGE_KEY)
    }
  } catch {
    // Storage may be unavailable in privacy-restricted contexts.
  }
}

/**
 * Keeps a long-lived SPA tab on the newest deployed fingerprinted bundle.
 * It checks on startup, focus/page restoration, and at a low-frequency interval.
 */
export function AppVersionMonitor({
  intervalMs = DEFAULT_CHECK_INTERVAL_MS,
  reload = reloadWithCacheBust,
}: {
  intervalMs?: number
  /** Test seam; production uses a cache-busted same-page navigation. */
  reload?: () => void
} = {}) {
  React.useEffect(() => {
    clearCacheBustParameter()

    const origin = window.location.origin
    const currentEntry = currentModuleEntry(origin)
    if (!currentEntry) return

    const attemptedTarget = readAttemptedTarget()
    if (attemptedTarget === currentEntry) writeAttemptedTarget(null)

    let disposed = false
    let checking = false

    const check = async () => {
      if (disposed || checking) return
      checking = true
      const latestEntry = await detectAvailableAppUpdate(currentEntry, origin)
      checking = false

      if (disposed || !latestEntry || latestEntry === readAttemptedTarget()) {
        return
      }

      writeAttemptedTarget(latestEntry)
      reload()
    }

    const checkWhenVisible = () => {
      if (document.visibilityState === "visible") void check()
    }

    window.addEventListener("focus", check)
    window.addEventListener("pageshow", check)
    document.addEventListener("visibilitychange", checkWhenVisible)
    const interval = window.setInterval(() => void check(), intervalMs)
    void check()

    return () => {
      disposed = true
      window.clearInterval(interval)
      window.removeEventListener("focus", check)
      window.removeEventListener("pageshow", check)
      document.removeEventListener("visibilitychange", checkWhenVisible)
    }
  }, [intervalMs, reload])

  return null
}
