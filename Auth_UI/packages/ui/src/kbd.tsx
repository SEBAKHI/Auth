import * as React from "react"

import { cn } from "@authsystem/ui/utils"

/**
 * A key cap. Used for the ⌘K hint on the search trigger and for the palette's
 * keyboard legend.
 *
 * `rounded-3xl` and the `h-5` cap match `Badge`, which is the closest sibling in
 * the preset — a key cap and a badge are the same class of inline chip, so they
 * should not disagree about their corners. `InputGroupAddon` already styles
 * `[data-slot=kbd]` for the in-field case, which is why the slot attribute is
 * not optional.
 */
function Kbd({ className, ...props }: React.ComponentProps<"kbd">) {
  return (
    <kbd
      data-slot="kbd"
      className={cn(
        "pointer-events-none inline-flex h-5 w-fit min-w-5 shrink-0 items-center justify-center gap-1 rounded-3xl bg-muted px-1.5 font-sans text-xs font-medium text-muted-foreground select-none [&_svg:not([class*='size-'])]:size-3",
        className
      )}
      {...props}
    />
  )
}

/** A chord: several caps read as one shortcut. */
function KbdGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="kbd-group"
      className={cn("inline-flex items-center gap-1", className)}
      {...props}
    />
  )
}

export { Kbd, KbdGroup }
