import * as React from "react"
import { Grip } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@authsystem/ui/popover"
import { Spinner } from "@authsystem/ui/spinner"

/*
 * Loaded on the first open: the tiles, their query and the logos stay out of
 * the shell's first download, and the list is fetched only while the panel is
 * open (the panel's content is not mounted while it is closed).
 */
const PanelTiles = React.lazy(() =>
  import("@authsystem/ui/common/application-tiles").then((module) => ({
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
        <Button variant="ghost" size="icon-header" aria-label={label}>
          <Grip />
        </Button>
      </PopoverTrigger>
      {/* Many applications scroll inside the panel, never past the screen:
          its height stops at what Radix measures free below the button, less
          the 8px it keeps from every edge of the screen.
          `[&>*]:shrink-0`: this column is a scroll pane, and a child that
          could shrink would be squashed instead of scrolling (layout.md). */}
      <PopoverContent
        align="end"
        collisionPadding={8}
        aria-label={label}
        className="max-h-[min(30rem,var(--radix-popover-content-available-height))] w-80 overflow-y-auto [&>*]:shrink-0"
      >
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
      </PopoverContent>
    </Popover>
  )
}
