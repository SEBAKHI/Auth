import { ChevronDown, type LucideIcon } from "lucide-react"

import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { Separator } from "@authsystem/ui/separator"
import { Spinner } from "@authsystem/ui/spinner"

export interface PageAction {
  id: string
  label: string
  onAction: () => void
  icon?: LucideIcon
  variant?: "default" | "outline" | "secondary" | "destructive"
  disabled?: boolean
  pending?: boolean
}

function ActionIcon({ action }: { action: PageAction }) {
  if (action.pending) {
    return <Spinner data-icon="inline-start" aria-hidden="true" />
  }
  const Icon = action.icon
  return Icon ? <Icon data-icon="inline-start" /> : null
}

/**
 * Renders one action contract as a wrapping desktop toolbar and a named
 * responsive menu. Destructive actions are always separated into a danger
 * group, while permissions remain the owning page's responsibility.
 */
export function PageActionSurface({
  actions,
  label,
}: {
  actions: PageAction[]
  label: string
}) {
  if (actions.length === 0) return null

  const regularActions = actions.filter(
    (action) => action.variant !== "destructive"
  )
  const destructiveActions = actions.filter(
    (action) => action.variant === "destructive"
  )

  return (
    <>
      <div
        role="group"
        aria-label={label}
        data-slot="page-action-surface-desktop"
        className="hidden flex-wrap items-center justify-end gap-2 xl:flex"
      >
        {regularActions.map((action) => (
          <Button
            key={action.id}
            type="button"
            variant={action.variant ?? "outline"}
            disabled={action.disabled || action.pending}
            onClick={action.onAction}
          >
            <ActionIcon action={action} />
            {action.label}
          </Button>
        ))}
        {regularActions.length > 0 && destructiveActions.length > 0 ? (
          <Separator orientation="vertical" className="h-6" />
        ) : null}
        {destructiveActions.map((action) => (
          <Button
            key={action.id}
            type="button"
            variant="destructive"
            disabled={action.disabled || action.pending}
            onClick={action.onAction}
          >
            <ActionIcon action={action} />
            {action.label}
          </Button>
        ))}
      </div>

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button type="button" variant="outline" className="xl:hidden">
            {label}
            <ChevronDown data-icon="inline-end" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-64">
          {regularActions.length > 0 ? (
            <DropdownMenuGroup>
              {regularActions.map((action) => {
                const Icon = action.icon
                return (
                  <DropdownMenuItem
                    key={action.id}
                    disabled={action.disabled || action.pending}
                    onSelect={action.onAction}
                  >
                    {action.pending ? (
                      <Spinner aria-hidden="true" />
                    ) : Icon ? (
                      <Icon />
                    ) : null}
                    {action.label}
                  </DropdownMenuItem>
                )
              })}
            </DropdownMenuGroup>
          ) : null}
          {regularActions.length > 0 && destructiveActions.length > 0 ? (
            <DropdownMenuSeparator />
          ) : null}
          {destructiveActions.length > 0 ? (
            <DropdownMenuGroup>
              {destructiveActions.map((action) => {
                const Icon = action.icon
                return (
                  <DropdownMenuItem
                    key={action.id}
                    variant="destructive"
                    disabled={action.disabled || action.pending}
                    onSelect={action.onAction}
                  >
                    {action.pending ? (
                      <Spinner aria-hidden="true" />
                    ) : Icon ? (
                      <Icon />
                    ) : null}
                    {action.label}
                  </DropdownMenuItem>
                )
              })}
            </DropdownMenuGroup>
          ) : null}
        </DropdownMenuContent>
      </DropdownMenu>
    </>
  )
}
