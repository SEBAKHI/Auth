import { ChevronDownIcon } from "lucide-react"
import * as React from "react"

import { cn } from "@astoom/ui/utils"

/**
 * Browser-native select control. Use this inside modal dialogs when the menu
 * must remain in the dialog's own focus and pointer-event boundary.
 *
 * The option list is drawn by the browser, not by us. `color-scheme` on the root
 * (see `preset.css`) is what makes it follow the active theme; the explicit
 * `option` colours below are a fallback for platforms that ignore it, without
 * which light-on-white option text was unreadable in dark mode.
 */
function NativeSelect({
  className,
  children,
  ...props
}: React.ComponentProps<"select">) {
  return (
    <div className="relative w-full" data-slot="native-select-wrapper">
      <select
        data-slot="native-select"
        className={cn(
          "h-9 w-full appearance-none rounded-3xl border border-transparent bg-input/50 px-3 pe-9 text-sm outline-none transition-[color,box-shadow,background-color] focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/30 disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 [&>option]:bg-popover [&>option]:text-popover-foreground",
          className
        )}
        {...props}
      >
        {children}
      </select>
      <ChevronDownIcon className="pointer-events-none absolute top-1/2 end-3 size-4 -translate-y-1/2 text-muted-foreground" />
    </div>
  )
}

export { NativeSelect }
