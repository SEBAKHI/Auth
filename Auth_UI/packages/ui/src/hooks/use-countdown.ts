import * as React from "react"

function remainingSeconds(target: Date | null): number {
  if (!target) return 0
  return Math.max(0, Math.ceil((target.getTime() - Date.now()) / 1000))
}

/**
 * Counts down to `target`, recomputing from the wall clock on every tick so
 * the value self-corrects after background-tab throttling. `label` is "mm:ss".
 */
export function useCountdown(target: Date | null): {
  totalSeconds: number
  label: string
  expired: boolean
} {
  const subscribe = React.useCallback(
    (onTick: () => void) => {
      if (!target) return () => {}
      const timer = window.setInterval(() => {
        onTick()
        if (remainingSeconds(target) <= 0) window.clearInterval(timer)
      }, 250)
      return () => window.clearInterval(timer)
    },
    [target]
  )
  const totalSeconds = React.useSyncExternalStore(subscribe, () =>
    remainingSeconds(target)
  )

  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  const label = `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`

  return { totalSeconds, label, expired: Boolean(target) && totalSeconds <= 0 }
}
