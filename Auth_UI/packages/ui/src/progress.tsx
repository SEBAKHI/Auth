"use client"

import * as React from "react"
import { Progress as ProgressPrimitive } from "radix-ui"

import { cn } from "@astoom/ui/utils"

/**
 * Determinate progress / meter.
 *
 * Upstream drives the fill with an inline `transform: translateX(-N%)`, which is
 * direction-blind: an inline style cannot be overridden by a `rtl:` utility, so
 * under RTL the bar would empty from the wrong edge. The offset is therefore
 * published as a custom property and consumed by a pair of logical utilities, so
 * the fill always grows from the inline start.
 */
function Progress({
  className,
  value,
  ...props
}: React.ComponentProps<typeof ProgressPrimitive.Root>) {
  return (
    <ProgressPrimitive.Root
      data-slot="progress"
      className={cn(
        "relative flex h-3 w-full items-center overflow-x-hidden rounded-full bg-muted",
        className
      )}
      {...props}
    >
      <ProgressPrimitive.Indicator
        data-slot="progress-indicator"
        className="size-full flex-1 -translate-x-(--progress-offset) bg-primary transition-all rtl:translate-x-(--progress-offset)"
        style={
          {
            "--progress-offset": `${100 - (value || 0)}%`,
          } as React.CSSProperties
        }
      />
    </ProgressPrimitive.Root>
  )
}

export { Progress }
