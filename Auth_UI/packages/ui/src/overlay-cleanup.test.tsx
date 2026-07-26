import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { AlertDialog, AlertDialogContent, AlertDialogTitle } from "./alert-dialog"
import { Dialog, DialogContent, DialogTitle } from "./dialog"
import { Sheet, SheetContent, SheetTitle } from "./sheet"

/**
 * Overlays must have NO exit animation: Radix Presence unmounts a closed
 * element synchronously only when its computed animation-name is "none";
 * with an exit animation it waits for animationend with no timeout, and a
 * same-batch re-render (e.g. a React Query invalidation after a confirm)
 * can swallow that event and strand an invisible overlay that blocks every
 * pointer event until reload. jsdom never runs CSS animations, so the
 * regression guard here is the class contract itself, not timing.
 */
const overlayCases = [
  {
    name: "AlertDialog",
    slot: "alert-dialog-overlay",
    ui: (open: boolean) => (
      <AlertDialog open={open} onOpenChange={() => undefined}>
        <AlertDialogContent>
          <AlertDialogTitle>title</AlertDialogTitle>
        </AlertDialogContent>
      </AlertDialog>
    ),
  },
  {
    name: "Dialog",
    slot: "dialog-overlay",
    ui: (open: boolean) => (
      <Dialog open={open} onOpenChange={() => undefined}>
        <DialogContent>
          <DialogTitle>title</DialogTitle>
        </DialogContent>
      </Dialog>
    ),
  },
  {
    name: "Sheet",
    slot: "sheet-overlay",
    ui: (open: boolean) => (
      <Sheet open={open} onOpenChange={() => undefined}>
        <SheetContent>
          <SheetTitle>title</SheetTitle>
        </SheetContent>
      </Sheet>
    ),
  },
]

describe.each(overlayCases)("$name overlay teardown", ({ slot, ui }) => {
  const overlay = () => document.querySelector(`[data-slot="${slot}"]`)

  it("declares no exit animation and a closed-state pointer-events guard", () => {
    render(ui(true))

    const className = overlay()?.className ?? ""
    expect(className).not.toMatch(/animate-out|fade-out/)
    expect(className).toContain("data-closed:pointer-events-none!")
  })

  it("unmounts the overlay synchronously when closed", () => {
    const { rerender } = render(ui(true))
    expect(overlay()).not.toBeNull()

    rerender(ui(false))
    expect(overlay()).toBeNull()
  })
})
