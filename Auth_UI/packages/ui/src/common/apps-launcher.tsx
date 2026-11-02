import * as React from "react"
import { useTranslation } from "react-i18next"

import { AppsGridButton, AppsPanelContent } from "@authsystem/ui/common/apps-panel"
import { Popover, PopoverTrigger } from "@authsystem/ui/popover"
import { Spinner } from "@authsystem/ui/spinner"

/*
 * Loaded on the first open: the tiles, their query and the logos stay out of
 * the shell's first download, and the list is fetched only while the panel is
 * open (the panel's content is not mounted while it is closed).
 */
const PanelTiles = React.lazy(() =>
  import("@authsystem/ui/common/my-applications").then((module) => ({
    default: module.MyApplicationsPanelTiles,
  }))
)

/**
 * The grid button beside the avatar in the header (owner, 2026-11-02), as on
 * Google's pages: it opens the person's applications as tiles in a panel, each
 * opening its application in a new tab. The My applications page keeps the
 * same tiles; this is the shortcut to them from any page.
 */
export function AppsLauncher() {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)
  const label = t("myApplications.launcher")

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <AppsGridButton label={label} />
      </PopoverTrigger>
      <AppsPanelContent label={label}>
        <React.Suspense
          fallback={
            <div className="flex justify-center py-6">
              <Spinner aria-label={t("common.loading")} />
            </div>
          }
        >
          {/* Closes once a tile is followed: the application opens in a new
              tab, and coming back should find the page as it was left. */}
          <PanelTiles onOpen={() => setOpen(false)} />
        </React.Suspense>
      </AppsPanelContent>
    </Popover>
  )
}
