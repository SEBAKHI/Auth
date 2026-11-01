import * as React from "react"

import { DropdownMenuItem } from "@authsystem/ui/dropdown-menu"

/**
 * A row-menu action this row cannot take, shown disabled with the reason written
 * under it rather than left out. A missing item reads as a missing feature: the
 * reader cannot tell "not allowed here" from "not possible anywhere", so they
 * go looking for it elsewhere (the active design that cannot be deleted, the
 * global layout, a published policy version).
 *
 * The reason is a visible line, not a tooltip: a disabled menu item takes neither
 * focus nor touch, so a tooltip reached only mouse users. It sits outside the item,
 * so the disabled item's fading does not fade the reason too, and the item's name
 * stays the action alone; the item points at it as its description.
 */
export function UnavailableMenuItem({
  reason,
  variant = "default",
  children,
}: {
  /** Why this row cannot take the action, and what to do instead when there is something. */
  reason: string
  variant?: "default" | "destructive"
  children: React.ReactNode
}) {
  const reasonId = React.useId()
  return (
    <div data-slot="unavailable-menu-item" className="flex flex-col">
      <DropdownMenuItem variant={variant} disabled aria-describedby={reasonId} className="pb-1">
        {children}
      </DropdownMenuItem>
      <p id={reasonId} className="px-3 pb-2 text-xs text-muted-foreground">
        {reason}
      </p>
    </div>
  )
}
