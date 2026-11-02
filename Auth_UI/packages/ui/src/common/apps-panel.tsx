import type * as React from "react"
import { Grip } from "lucide-react"

import { Button } from "@authsystem/ui/button"
import { PopoverContent } from "@authsystem/ui/popover"
import { cn } from "@authsystem/ui/utils"

/*
 * The grid button and the panel it opens, as the header draws them
 * (`AppsLauncher`) and as a linked application's account bar draws them in its
 * frames. No API client here: the bar's frames load this module.
 */

/** The header's grid button. */
export function AppsGridButton({
  label,
  ...props
}: Omit<React.ComponentProps<typeof Button>, "children"> & { label: string }) {
  return (
    <Button variant="ghost" size="icon-header" aria-label={label} {...props}>
      <Grip />
    </Button>
  )
}

/**
 * The panel of tiles. Many applications scroll inside it, never past the
 * screen: its height stops at what Radix measures free below the button, less
 * the 8px it keeps from every edge of the screen (a frame passes its own bound
 * as `style.maxHeight`). `[&>*]:shrink-0`: this column is a scroll pane, and a
 * child that could shrink would be squashed instead of scrolling (layout.md).
 */
export function AppsPanelContent({
  label,
  className,
  ...props
}: React.ComponentProps<typeof PopoverContent> & { label: string }) {
  return (
    <PopoverContent
      align="end"
      collisionPadding={8}
      aria-label={label}
      className={cn(
        "max-h-[min(30rem,var(--radix-popover-content-available-height))] w-80 overflow-y-auto [&>*]:shrink-0",
        className
      )}
      {...props}
    />
  )
}
