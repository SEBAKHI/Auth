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
import { Spinner } from "@authsystem/ui/spinner"

export interface PageAction {
  id: string
  label: string
  onAction: () => void
  icon?: LucideIcon
  /**
   * How the action looks when it is rendered as a button. `default` is the
   * filled primary treatment and implies {@link PageAction.promoted}, since an
   * action drawn as the page's primary has to be on the page.
   */
  variant?: "default" | "outline" | "secondary" | "destructive"
  /**
   * Keep this action out of the menu, as a button of its own.
   *
   * Separate from `variant` because the two answer different questions - WHERE
   * an action renders and HOW it looks. The template editor needs Save draft in
   * reach on every keystroke without it becoming a second filled primary beside
   * Publish, and that is only expressible once the two are unhooked.
   *
   * Spend it sparingly: every promotion is one more thing competing for the
   * reader's attention, which is the crowding this surface exists to undo.
   */
  promoted?: boolean
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

function MenuIcon({ action }: { action: PageAction }) {
  if (action.pending) return <Spinner aria-hidden="true" />
  const Icon = action.icon
  return Icon ? <Icon /> : null
}

/** An action stays out of the menu when it says so, or when it is the primary. */
function isPromoted(action: PageAction) {
  return action.promoted === true || action.variant === "default"
}

/**
 * Renders one action contract as a short row of promoted buttons plus a named
 * menu holding everything else. Destructive actions are always separated into a
 * danger group, while permissions remain the owning page's responsibility.
 *
 * One surface at every width, not a toolbar that collapses at `xl`. Laying the
 * whole contract out as buttons put eight of them across the top of a record -
 * a wall that reads as noise rather than as a set of choices, and that pushed
 * the record's own detail down the page. A reader scanning a user does not want
 * eight verbs; they want the one they came for, and somewhere obvious to look
 * for the rest.
 *
 * The trade is discoverability: an action behind a menu is one click further
 * away and invisible until asked for. That is why the trigger carries a real
 * name ("Actions") rather than an icon, why nothing is hidden by width, and why
 * a page can pull its few working actions out of the menu - the common cases
 * cost no clicks at all. Promotion is the page's judgement about what its
 * readers do repeatedly, not a general escape hatch.
 */
export function PageActionSurface({
  actions,
  label,
}: {
  actions: PageAction[]
  label: string
}) {
  if (actions.length === 0) return null

  // Contract order throughout: the page decided that Save comes before Publish,
  // and re-sorting here would quietly overrule it.
  const promotedActions = actions.filter(isPromoted)
  const menuActions = actions.filter((action) => !isPromoted(action))

  const regularActions = menuActions.filter(
    (action) => action.variant !== "destructive"
  )
  const destructiveActions = menuActions.filter(
    (action) => action.variant === "destructive"
  )

  return (
    <>
      {promotedActions.map((action) => (
        <Button
          key={action.id}
          type="button"
          data-slot="page-action-surface-action"
          variant={action.variant ?? "outline"}
          disabled={action.disabled || action.pending}
          onClick={action.onAction}
        >
          <ActionIcon action={action} />
          {action.label}
        </Button>
      ))}

      {menuActions.length > 0 ? (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              type="button"
              data-slot="page-action-surface-menu"
              variant="outline"
            >
              {label}
              <ChevronDown data-icon="inline-end" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-64">
            {regularActions.length > 0 ? (
              <DropdownMenuGroup>
                {regularActions.map((action) => (
                  <DropdownMenuItem
                    key={action.id}
                    disabled={action.disabled || action.pending}
                    onSelect={action.onAction}
                  >
                    <MenuIcon action={action} />
                    {action.label}
                  </DropdownMenuItem>
                ))}
              </DropdownMenuGroup>
            ) : null}
            {regularActions.length > 0 && destructiveActions.length > 0 ? (
              <DropdownMenuSeparator />
            ) : null}
            {destructiveActions.length > 0 ? (
              <DropdownMenuGroup>
                {destructiveActions.map((action) => (
                  <DropdownMenuItem
                    key={action.id}
                    variant="destructive"
                    disabled={action.disabled || action.pending}
                    onSelect={action.onAction}
                  >
                    <MenuIcon action={action} />
                    {action.label}
                  </DropdownMenuItem>
                ))}
              </DropdownMenuGroup>
            ) : null}
          </DropdownMenuContent>
        </DropdownMenu>
      ) : null}
    </>
  )
}
