import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { AppsGridButton } from "@authsystem/ui/common/apps-panel"
import { UserAvatarButton } from "@authsystem/ui/common/user-identity"
import { Skeleton } from "@authsystem/ui/skeleton"

import { useEmbed } from "./embed-context"
import { HIDE_VALUES, onHostMessage, oneOf, tellHost, type Panel } from "./protocol"
import { useReportedSize } from "./use-reported-size"

/**
 * The bar a linked application shows in its header (owner decisions of 2026-11-02): the
 * grid button and the avatar, drawn as the accounts app's own header draws them and at the
 * platform's header icon size. Either can be hidden (`hide="apps"`, `hide="avatar"`). With
 * no single sign-on session, a Sign in button takes the avatar's place, and the grid goes:
 * there are no applications to list for nobody.
 *
 * It opens nothing itself. A button asks the loader to open or close its panel, and the
 * loader says back which panel is open, so the buttons announce the truth
 * (`aria-expanded`); Sign in asks the loader to take the application's page to its
 * sign-in. The frame never navigates the page.
 */
export function AccountBar() {
  const { t } = useTranslation()
  const { parentOrigin, account } = useEmbed()
  const hide = oneOf(HIDE_VALUES, new URLSearchParams(window.location.search).get("hide"))
  const [open, setOpen] = React.useState<Panel | null>(null)
  const appsRef = React.useRef<HTMLButtonElement>(null)
  const avatarRef = React.useRef<HTMLButtonElement>(null)
  // Inset 4: the `p-1` below, so the panels line up with the buttons, not the frame.
  const rootRef = useReportedSize(parentOrigin, 0, 4)

  React.useEffect(
    () =>
      onHostMessage(parentOrigin, (message) => {
        if (message.type !== "state") return
        setOpen((previous) => {
          // A panel closed from the keyboard (Escape, or after a tile was followed):
          // the focus returns to the button that opened it.
          if (message.focus && previous) {
            ;(previous === "apps" ? appsRef : avatarRef).current?.focus()
          }
          return message.open
        })
      }),
    [parentOrigin]
  )

  const toggle = (panel: Panel) => tellHost(parentOrigin, { type: "toggle", panel })
  const showApps = hide !== "apps"
  const showAvatar = hide !== "avatar"

  let content: React.ReactNode = null
  if (account.isPending) {
    content = (
      <>
        {showApps ? <Skeleton className="size-header-button rounded-full" /> : null}
        {showAvatar ? <Skeleton className="size-header-button rounded-full" /> : null}
      </>
    )
  } else if (account.data === null) {
    content = showAvatar ? (
      <Button onClick={() => tellHost(parentOrigin, { type: "signin" })}>{t("auth.signIn")}</Button>
    ) : null
  } else if (account.data) {
    const name = account.data.name ?? ""
    content = (
      <>
        {showApps ? (
          <AppsGridButton
            ref={appsRef}
            label={t("myApplications.launcher")}
            aria-haspopup="dialog"
            aria-expanded={open === "apps"}
            onClick={() => toggle("apps")}
          />
        ) : null}
        {showAvatar ? (
          <UserAvatarButton
            ref={avatarRef}
            name={name}
            src={account.data.profileImageUrl}
            aria-haspopup="menu"
            aria-expanded={open === "account"}
            onClick={() => toggle("account")}
          />
        ) : null}
      </>
    )
  }
  // A failed read draws nothing: a refusal or an outage is not "signed out".

  // Nothing to draw measures 0 × 0, and the loader then hides the frame.
  if (!content) return <div ref={rootRef} className="inline-flex w-max" />

  return (
    // `p-1`: room for the focus ring, which the frame's edge would otherwise cut off.
    // `w-max`: as wide as the buttons whatever the frame's width, which follows this.
    <div ref={rootRef} className="inline-flex w-max items-center gap-1 p-1">
      {content}
    </div>
  )
}
