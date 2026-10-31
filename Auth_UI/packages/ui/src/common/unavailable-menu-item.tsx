import type * as React from "react"

import { DropdownMenuItem } from "@authsystem/ui/dropdown-menu"
import { Tooltip, TooltipContent, TooltipTrigger } from "@authsystem/ui/tooltip"

/**
 * A row-menu action this row cannot take, shown disabled with the reason on
 * hover rather than left out. A missing item reads as a missing feature: the
 * reader cannot tell "not allowed here" from "not possible anywhere", so they
 * go looking for it elsewhere (the active design that cannot be deleted, the
 * global layout, a published policy version).
 *
 * A disabled menu item takes no pointer events, so the tooltip is anchored on
 * a wrapper that does.
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
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span data-slot="unavailable-menu-item">
          <DropdownMenuItem variant={variant} disabled>
            {children}
          </DropdownMenuItem>
        </span>
      </TooltipTrigger>
      {/* Below: beside the menu it ran off a phone's edge, and above it covered the item
          above it. A refused action is the menu's last group, so below is free. */}
      <TooltipContent side="bottom" className="max-w-64">
        {reason}
      </TooltipContent>
    </Tooltip>
  )
}
