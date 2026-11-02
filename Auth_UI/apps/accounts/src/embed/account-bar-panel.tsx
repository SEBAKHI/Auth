import { LogOut, User as UserIcon } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { ApplicationTiles } from "@authsystem/ui/common/application-tiles"
import { AppsPanelContent } from "@authsystem/ui/common/apps-panel"
import { USER_MENU_CONTENT_CLASS, UserMenuIdentity } from "@authsystem/ui/common/user-identity"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { Popover, PopoverAnchor } from "@authsystem/ui/popover"
import { cn } from "@authsystem/ui/utils"

import { useEmbed } from "./embed-context"
import { onHostMessage, oneOf, PANELS, tellHost } from "./protocol"
import { useReportedSize } from "./use-reported-size"

/**
 * Room around the panel inside its frame, for its shadow; reported to the loader with the
 * size, so the panel itself, not its frame, lines up under the bar.
 */
const INSET = 16

/** The tallest a panel grows before it scrolls, as the header's panel (30rem). */
const MAX_PANEL_HEIGHT = 480

/** Where Radix places the panel: a zero-height line across the frame, INSET from its edges. */
const ANCHOR_CLASS = "pointer-events-none fixed inset-x-4 top-4 h-0"

/**
 * A panel of the account bar, in its own frame: the applications as tiles (`panel=apps`),
 * or the person's menu (`panel=account`): name and email, Profile in a new tab, and Sign
 * out when the application gave a sign-out address. Drawn by the same Popover and menu as
 * the accounts app's header, open for as long as the frame is shown; whatever would close
 * them there (Escape, a click beside them, a choice) asks the loader to close the frame.
 */
export function AccountBarPanel() {
  const params = new URLSearchParams(window.location.search)
  const panel = oneOf(PANELS, params.get("panel"))
  const { parentOrigin } = useEmbed()
  const [shown, setShown] = React.useState({ count: 0, maxHeight: MAX_PANEL_HEIGHT })

  React.useEffect(
    () =>
      onHostMessage(parentOrigin, (message) => {
        if (message.type !== "opened") return
        const bound = Number.isFinite(message.maxHeight) ? Math.max(120, message.maxHeight) : MAX_PANEL_HEIGHT
        // A new key each time: the content mounts again, so Radix moves the focus into it.
        setShown((current) => ({ count: current.count + 1, maxHeight: Math.min(MAX_PANEL_HEIGHT, bound) }))
      }),
    [parentOrigin]
  )

  const close = (focusBar: boolean) => tellHost(parentOrigin, { type: "close", focusBar })

  if (panel === "apps") return <AppsPanel key={shown.count} maxHeight={shown.maxHeight} close={close} />
  if (panel === "account")
    return (
      <AccountPanel
        key={shown.count}
        maxHeight={shown.maxHeight}
        close={close}
        signOut={params.get("signout") === "1"}
      />
    )
  return null
}

type PanelProps = { maxHeight: number; close: (focusBar: boolean) => void }

function AppsPanel({ maxHeight, close }: PanelProps) {
  const { t } = useTranslation()
  const { parentOrigin, account } = useEmbed()
  const ref = useReportedSize(parentOrigin, INSET)
  const label = t("myApplications.launcher")

  return (
    <Popover open onOpenChange={(open) => !open && close(false)}>
      <PopoverAnchor className={ANCHOR_CLASS} />
      <AppsPanelContent
        ref={ref}
        label={label}
        align="center"
        sideOffset={0}
        avoidCollisions={false}
        // The host's bound, not Radix's: Radix measures this frame, which is as tall as the panel.
        style={{ maxHeight }}
        className="dark:[color-scheme:dark]"
        // This frame holds nothing but the panel: focus "leaving" it is the loader focusing
        // the frame, not the person going elsewhere. A press beside it, or Escape, closes it.
        onFocusOutside={(event) => event.preventDefault()}
        onEscapeKeyDown={(event) => {
          event.preventDefault()
          close(true)
        }}
      >
        <ApplicationTiles
          state={{
            data: account.data?.applications,
            isPending: account.isPending,
            isError: account.isError,
            refetch: account.refetch,
          }}
          label={label}
          variant="panel"
          // The application opens in a new tab; coming back finds the page as it was left.
          onOpen={() => close(false)}
        />
      </AppsPanelContent>
    </Popover>
  )
}

function AccountPanel({ maxHeight, close, signOut }: PanelProps & { signOut: boolean }) {
  const { t } = useTranslation()
  const { parentOrigin, account } = useEmbed()
  const ref = useReportedSize(parentOrigin, INSET)
  const person = account.data

  return (
    <DropdownMenu
      open
      modal={false}
      // Radix closes a menu whose window loses focus. This window is a frame, and focus
      // moves between it, the bar and the application as the panel opens; the loader closes
      // the panel on a press on the application's page. So a close counts only while this
      // frame has the focus: a choice, or a press beside the menu (Escape is below).
      onOpenChange={(open) => !open && document.hasFocus() && close(false)}
    >
      <DropdownMenuTrigger asChild>
        <span aria-hidden="true" tabIndex={-1} className={ANCHOR_CLASS} />
      </DropdownMenuTrigger>
      <DropdownMenuContent
        ref={ref}
        align="center"
        sideOffset={0}
        avoidCollisions={false}
        style={{ maxHeight }}
        className={cn(USER_MENU_CONTENT_CLASS, "dark:[color-scheme:dark]")}
        // As the applications panel: only a press beside it, Escape or a choice closes it.
        onFocusOutside={(event) => event.preventDefault()}
        onEscapeKeyDown={(event) => {
          event.preventDefault()
          close(true)
        }}
      >
        {person ? (
          <UserMenuIdentity name={person.name ?? ""} email={person.email} src={person.profileImageUrl} />
        ) : null}
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuItem asChild>
            {/* The accounts app's own profile, in a new tab: this page stays the application's. */}
            <a href="/profile" target="_blank" rel="noopener noreferrer">
              <UserIcon />
              {t("common.profile")}
              <span className="sr-only">{t("myApplications.opensInNewTab")}</span>
            </a>
          </DropdownMenuItem>
        </DropdownMenuGroup>
        {signOut ? (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuGroup>
              {/* The application's own sign-out, which ends its session and then IAM's
                  (owner decision 4). The loader takes the page there; never this frame. */}
              <DropdownMenuItem onSelect={() => tellHost(parentOrigin, { type: "signout" })}>
                <LogOut />
                {t("common.signOut")}
              </DropdownMenuItem>
            </DropdownMenuGroup>
          </>
        ) : null}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
