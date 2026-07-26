"use client"

import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { Dialog as DialogPrimitive } from "radix-ui"

import { cn } from "@astoom/ui/utils"
import { Button } from "@astoom/ui/button"
import { XIcon } from "lucide-react"

function Dialog({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Root>) {
  return <DialogPrimitive.Root data-slot="dialog" {...props} />
}

function DialogTrigger({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Trigger>) {
  return <DialogPrimitive.Trigger data-slot="dialog-trigger" {...props} />
}

function DialogPortal({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Portal>) {
  return <DialogPrimitive.Portal data-slot="dialog-portal" {...props} />
}

function DialogClose({
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Close>) {
  return <DialogPrimitive.Close data-slot="dialog-close" {...props} />
}

function DialogOverlay({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Overlay>) {
  return (
    <DialogPrimitive.Overlay
      data-slot="dialog-overlay"
      className={cn(
        // No exit animation on purpose — see AlertDialogOverlay: an exit
        // animation lets Radix Presence strand a click-blocking overlay when
        // animationend is lost mid-re-render.
        "fixed inset-0 isolate z-50 bg-black/30 duration-100 data-open:animate-in data-open:fade-in-0 data-closed:pointer-events-none!",
        className
      )}
      {...props}
    />
  )
}

/**
 * Radix portals Select/Dropdown/Popover content outside the dialog DOM, so a
 * click inside one of those poppers reads as an "outside" interaction and would
 * dismiss the dialog. Treat interactions that originate inside a popper portal
 * as inside the dialog so the dialog only closes on a genuine outside click.
 */
function isEventFromPopper(
  event: CustomEvent<{ originalEvent: Event }>
): boolean {
  const target = event.detail.originalEvent.target as Element | null
  return Boolean(
    target?.closest?.(
      "[data-radix-popper-content-wrapper],[data-slot='select-content'],[data-slot='dropdown-menu-content']"
    )
  )
}

/**
 * One width scale for every dialog in both apps, so a dialog is sized by
 * declaring what it holds rather than by a per-call-site `sm:max-w-*` string.
 *
 * `overflow-x-hidden` is deliberate: `overflow-y-auto` alone computes overflow-x
 * to `auto`, so any child that outgrows the dialog produced a horizontal
 * scrollbar instead of wrapping. Content is expected to reflow, never to pan.
 *
 * Centering is physical (`left-1/2` + `-translate-x-1/2`) and the small-screen
 * cap is in `svw`, not `%`, on purpose. Centering with the logical `start-1/2`
 * plus an `rtl:` translate override made the dialog overflow the viewport in
 * RTL; the overflow widened the initial containing block, the percentage cap
 * then resolved against that wider box, and each step fed the next. Viewport
 * units and direction-agnostic centering break that loop — a centered box needs
 * no writing direction anyway.
 */
const dialogContentVariants = cva(
  "fixed left-1/2 top-1/2 z-50 grid max-h-[calc(100dvh-2rem)] w-full min-w-0 max-w-[calc(100svw-2rem)] -translate-x-1/2 -translate-y-1/2 gap-6 overflow-x-hidden overflow-y-auto rounded-4xl bg-popover p-6 text-sm text-popover-foreground shadow-xl ring-1 ring-foreground/5 duration-100 outline-none dark:ring-foreground/10 data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95",
  {
    variants: {
      size: {
        /** Confirmations and other single-sentence prompts. */
        sm: "sm:max-w-sm",
        /** A couple of short fields. */
        md: "sm:max-w-md",
        /** Default: a form, a picker plus a list, most editors. */
        lg: "sm:max-w-lg",
        /** Multi-column forms and side-by-side fields. */
        xl: "sm:max-w-2xl",
        /** Tables, diffs, long record detail. */
        "2xl": "sm:max-w-4xl",
        /** Editors that want the whole viewport minus a margin. */
        full: "sm:max-w-[min(72rem,calc(100%-4rem))]",
      },
    },
    defaultVariants: {
      size: "lg",
    },
  }
)

function DialogContent({
  className,
  children,
  size,
  showCloseButton = true,
  onPointerDownOutside,
  onInteractOutside,
  onFocusOutside,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Content> &
  VariantProps<typeof dialogContentVariants> & {
    showCloseButton?: boolean
  }) {
  return (
    <DialogPortal>
      <DialogOverlay />
      <DialogPrimitive.Content
        data-slot="dialog-content"
        onPointerDownOutside={(event) => {
          if (isEventFromPopper(event)) event.preventDefault()
          onPointerDownOutside?.(event)
        }}
        onInteractOutside={(event) => {
          if (isEventFromPopper(event)) event.preventDefault()
          onInteractOutside?.(event)
        }}
        onFocusOutside={(event) => {
          if (isEventFromPopper(event)) event.preventDefault()
          onFocusOutside?.(event)
        }}
        className={cn(dialogContentVariants({ size }), className)}
        {...props}
      >
        {children}
        {showCloseButton && (
          <DialogPrimitive.Close data-slot="dialog-close" asChild>
            <Button
              variant="ghost"
              className="absolute end-4 top-4 bg-secondary"
              size="icon-sm"
            >
              <XIcon />
              <span className="sr-only">Close</span>
            </Button>
          </DialogPrimitive.Close>
        )}
      </DialogPrimitive.Content>
    </DialogPortal>
  )
}

function DialogHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-header"
      className={cn("flex flex-col gap-1.5", className)}
      {...props}
    />
  )
}

function DialogFooter({
  className,
  showCloseButton = false,
  children,
  ...props
}: React.ComponentProps<"div"> & {
  showCloseButton?: boolean
}) {
  return (
    <div
      data-slot="dialog-footer"
      className={cn(
        "flex flex-col-reverse gap-2 sm:flex-row sm:justify-end",
        className
      )}
      {...props}
    >
      {children}
      {showCloseButton && (
        <DialogPrimitive.Close asChild>
          <Button variant="outline">Close</Button>
        </DialogPrimitive.Close>
      )}
    </div>
  )
}

function DialogTitle({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Title>) {
  return (
    <DialogPrimitive.Title
      data-slot="dialog-title"
      className={cn(
        "font-heading text-base leading-none font-medium",
        className
      )}
      {...props}
    />
  )
}

function DialogDescription({
  className,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Description>) {
  return (
    <DialogPrimitive.Description
      data-slot="dialog-description"
      className={cn(
        "text-sm text-muted-foreground *:[a]:underline *:[a]:underline-offset-3 *:[a]:hover:text-foreground",
        className
      )}
      {...props}
    />
  )
}

export {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogOverlay,
  DialogPortal,
  DialogTitle,
  DialogTrigger,
}
