import * as React from "react"
import { Command as CommandPrimitive } from "cmdk"

import { cn } from "@authsystem/ui/utils"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { InputGroup, InputGroupAddon } from "@authsystem/ui/input-group"
import { SearchIcon, CheckIcon } from "lucide-react"

function Command({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive>) {
  return (
    <CommandPrimitive
      data-slot="command"
      className={cn(
        // `min-h-0` so the list inside can give up height when the dialog is
        // capped — a flex/grid child refuses to shrink below its content
        // otherwise, and the footer ends up below the fold.
        "flex size-full min-h-0 flex-col overflow-hidden rounded-4xl bg-popover p-1 text-popover-foreground",
        className
      )}
      {...props}
    />
  )
}

function CommandDialog({
  title = "Command Palette",
  description = "Search for a command to run...",
  children,
  className,
  size = "xl",
  showCloseButton = false,
  onEscapeKeyDown,
  ...props
}: React.ComponentProps<typeof Dialog> & {
  title?: string
  description?: string
  className?: string
  size?: React.ComponentProps<typeof DialogContent>["size"]
  showCloseButton?: boolean
  /**
   * Forwarded to the content so a palette can spend the first Escape on
   * clearing its query instead of closing.
   */
  onEscapeKeyDown?: React.ComponentProps<typeof DialogContent>["onEscapeKeyDown"]
}) {
  return (
    <Dialog {...props}>
      <DialogContent
        onEscapeKeyDown={onEscapeKeyDown}
        className={cn(
          // `gap-0`: DialogContent is a grid with `gap-6`, and the sr-only
          // header still occupies a grid track — without this the palette
          // opens with 24px of dead space above the input.
          //
          // The height cap has to account for the offset: sitting a third of
          // the way down and then allowing a full viewport of height puts the
          // footer below the fold on a short screen. What is left below the
          // offset is two thirds, less the bottom margin.
          // `flex` rather than the inherited `grid`: an auto grid track is
          // sized to its content and will not shrink under the cap, so the
          // list keeps its full height and pushes the footer out of the box.
          "top-1/3 flex max-h-[calc(66.6dvh-1rem)] translate-y-0 flex-col gap-0 overflow-hidden rounded-4xl! p-0",
          className
        )}
        size={size}
        showCloseButton={showCloseButton}
      >
        {/* Inside the content, not beside it: Radix resolves the dialog's
            accessible name from a title in its own subtree, and a header
            rendered as a sibling of Content is not portalled with it. */}
        <DialogHeader className="sr-only">
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {children}
      </DialogContent>
    </Dialog>
  )
}

function CommandInput({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Input>) {
  return (
    <div data-slot="command-input-wrapper" className="p-1 pb-0">
      <InputGroup className="h-9 bg-input/50">
        <CommandPrimitive.Input
          data-slot="command-input"
          className={cn(
            "w-full text-sm outline-hidden disabled:cursor-not-allowed disabled:opacity-50",
            className
          )}
          {...props}
        />
        <InputGroupAddon>
          <SearchIcon className="size-4 shrink-0 opacity-50" />
        </InputGroupAddon>
      </InputGroup>
    </div>
  )
}

function CommandList({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.List>) {
  return (
    <CommandPrimitive.List
      data-slot="command-list"
      // The native scrollbar is left visible: in a short picker it never
      // appears anyway, and in a list long enough to scroll it is the only cue
      // that there is more below. Callers that want it hidden pass the
      // `no-scrollbar` shadcn utility (defined in shadcn/tailwind.css, which
      // each app's index.css imports — grepping *.css in this repo finds
      // nothing).
      className={cn(
        "max-h-72 scroll-py-1 overflow-x-hidden overflow-y-auto outline-none",
        className
      )}
      {...props}
    />
  )
}

function CommandEmpty({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Empty>) {
  return (
    <CommandPrimitive.Empty
      data-slot="command-empty"
      className={cn("py-6 text-center text-sm", className)}
      {...props}
    />
  )
}

/**
 * A run of items under one heading.
 *
 * Two things here fix "the rows all blend together". First, the items box is a
 * flex column with a gap: without it consecutive items are literally contiguous
 * boxes — row N's padding abuts row N+1's — and only a hover tint tells them
 * apart. 4px of gap against 8px of row padding gives proximity enough contrast
 * to bind each row's own lines together and hold them apart from the next,
 * which is what a divider between rows would otherwise have to do (and none of
 * the palettes worth copying draw one).
 *
 * Second, the heading sticks, so a long run of results never scrolls away from
 * the name of the section it belongs to. That needs `overflow-hidden` gone —
 * an `overflow` other than `visible` makes this element the sticky scroll
 * container, and the heading then sticks to a box that never scrolls. Dropping
 * it is safe: cmdk hides a group with the `hidden` attribute, not by clipping.
 */
function CommandGroup({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Group>) {
  return (
    <CommandPrimitive.Group
      data-slot="command-group"
      className={cn(
        "p-1.5 text-foreground",
        "**:[[cmdk-group-items]]:flex **:[[cmdk-group-items]]:flex-col **:[[cmdk-group-items]]:gap-1",
        "**:[[cmdk-group-heading]]:sticky **:[[cmdk-group-heading]]:top-0 **:[[cmdk-group-heading]]:z-10 **:[[cmdk-group-heading]]:bg-popover",
        "**:[[cmdk-group-heading]]:flex **:[[cmdk-group-heading]]:items-center **:[[cmdk-group-heading]]:gap-2 **:[[cmdk-group-heading]]:px-3 **:[[cmdk-group-heading]]:py-2 **:[[cmdk-group-heading]]:text-xs **:[[cmdk-group-heading]]:font-medium **:[[cmdk-group-heading]]:text-muted-foreground",
        className
      )}
      {...props}
    />
  )
}

function CommandSeparator({
  className,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Separator>) {
  return (
    <CommandPrimitive.Separator
      data-slot="command-separator"
      className={cn("my-1.5 h-px bg-border/50", className)}
      {...props}
    />
  )
}

function CommandItem({
  className,
  children,
  ...props
}: React.ComponentProps<typeof CommandPrimitive.Item>) {
  return (
    <CommandPrimitive.Item
      data-slot="command-item"
      className={cn(
        "group/command-item relative flex cursor-default items-center gap-2 rounded-2xl px-3 py-2 text-sm font-medium outline-hidden select-none in-data-[slot=dialog-content]:rounded-3xl data-[disabled=true]:pointer-events-none data-[disabled=true]:opacity-50 data-selected:bg-muted data-selected:text-foreground [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4 data-selected:*:[svg]:text-foreground",
        // A leading icon is a kind marker, not an accent: it recedes until the
        // row is the active one. Scoped to icons that state no colour of their
        // own, so a caller can still colour one deliberately.
        "[&_svg:not([class*='text-'])]:text-muted-foreground",
        // The active row is marked twice — tint plus a bar at the inline start
        // — because `--muted` against `--background` is barely one to one, so
        // the tint cannot carry the state on its own.
        //
        // The bar is a fixed 16px, centred: a length derived from the row would
        // make the marker report how tall the row is rather than that it is
        // selected, and a row whose radius clamps to half its height (every
        // single-line row in the dialog) would get no marker at all.
        //
        // What keeps it clear of the rounded corner is the 4px inline inset,
        // not the length. 4px wide rather than 2px because a 2px line lands on
        // fractional device pixels at the fractional display scales Windows
        // ships by default, and the colour fringe that produces is then half
        // the width of the mark — which is what reads as a blue tint on a mark
        // that has no chroma at all. `start-*` needs no RTL override.
        "data-selected:before:absolute data-selected:before:start-1 data-selected:before:top-1/2 data-selected:before:-mt-2 data-selected:before:h-4 data-selected:before:w-1 data-selected:before:rounded-full data-selected:before:bg-foreground",
        className
      )}
      {...props}
    >
      {children}
      <CheckIcon className="ms-auto opacity-0 group-has-data-[slot=command-shortcut]/command-item:hidden group-data-[checked=true]/command-item:opacity-100" />
    </CommandPrimitive.Item>
  )
}

/**
 * The text column of a multi-line item.
 *
 * `font-normal` is the load-bearing class. `CommandItem` sets `font-medium` on
 * the row — correct for the single-line pickers that make up most callers — and
 * every descendant inherits it, so a title, a hint and a location trail all
 * render at the same weight and the row reads as three equally important lines.
 * Resetting here and re-declaring weight on the title alone is what restores
 * the hierarchy; `CommandItemTitle` is the only thing in a row that is bold.
 *
 * `min-w-0` is not optional: a flex child defaults to `min-width: auto` and
 * refuses to shrink, so without it long text pushes the row wider instead of
 * ellipsing.
 */
function CommandItemContent({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="command-item-content"
      className={cn(
        "flex min-w-0 flex-1 flex-col gap-0.5 font-normal",
        className
      )}
      {...props}
    />
  )
}

/** The name of the thing. One line, always first, the only weighted text. */
function CommandItemTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="command-item-title"
      className={cn("truncate text-sm/6 font-medium text-foreground", className)}
      {...props}
    />
  )
}

/**
 * What the thing does. Prose, so it keeps the body size and wraps to a second
 * line rather than being cut mid-sentence — a hint clipped at one line costs
 * the reader exactly the words they needed to decide whether to open it.
 *
 * It goes full-contrast on the active row: muted foreground over the selected
 * tint falls under the contrast floor, and swapping the token is the fix.
 */
function CommandItemDescription({
  className,
  ...props
}: React.ComponentProps<"p">) {
  return (
    <p
      data-slot="command-item-description"
      className={cn(
        "line-clamp-2 text-start text-sm/6 text-muted-foreground group-data-selected/command-item:text-foreground",
        className
      )}
      {...props}
    />
  )
}

/**
 * Where the thing lives, under its name rather than across the row from it.
 *
 * A trail pushed to the far edge with `ms-auto` ends up an entire panel width
 * away from the title it annotates — and in RTL it lands on the opposite side
 * of the screen. Under the title it stays attached to it in every direction.
 *
 * The crumbs are separate elements on purpose. Joined into one string, a trail
 * whose parts are all Latin resolves to LTR inside an RTL row and renders
 * back-to-front; as flex children each crumb is its own bidi paragraph and
 * orders correctly with no isolation marks.
 */
function CommandItemTrail({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="command-item-trail"
      className={cn(
        "flex min-w-0 items-center gap-1 overflow-hidden text-xs/5 text-muted-foreground group-data-selected/command-item:text-foreground",
        // Arabic at 12px merges its dot clusters in a dense list; the trail is
        // the only thing in the row small enough for that to bite.
        "rtl:text-[0.8125rem]",
        className
      )}
      {...props}
    />
  )
}

/**
 * One step of a trail. The first crumb is the one allowed to shrink: the root
 * is the expendable end of a path, while the immediate parent is the part that
 * actually says which result this is.
 */
function CommandItemCrumb({
  className,
  first,
  ...props
}: React.ComponentProps<"span"> & { first?: boolean }) {
  return (
    <span
      data-slot="command-item-crumb"
      className={cn(
        first ? "min-w-0 shrink truncate" : "shrink-0 whitespace-nowrap",
        className
      )}
      {...props}
    />
  )
}

/**
 * The glyph between two crumbs. U+203A is bidi-mirrored, so the browser flips
 * it under RTL on its own — swapping in a left-pointing character by hand
 * double-flips it and points the trail the wrong way.
 */
function CommandItemCrumbSeparator({
  className,
  ...props
}: React.ComponentProps<"span">) {
  return (
    <span
      aria-hidden="true"
      role="presentation"
      className={cn("shrink-0 opacity-60", className)}
      {...props}
    >
      ›
    </span>
  )
}

/** The keyboard legend, pinned under the list. */
function CommandFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="command-footer"
      className={cn(
        "flex shrink-0 items-center gap-4 border-t px-4 py-2 text-xs text-muted-foreground",
        className
      )}
      {...props}
    />
  )
}

function CommandShortcut({
  className,
  ...props
}: React.ComponentProps<"span">) {
  return (
    <span
      data-slot="command-shortcut"
      className={cn(
        "ms-auto text-xs tracking-widest text-muted-foreground group-data-selected/command-item:text-foreground",
        className
      )}
      {...props}
    />
  )
}

export {
  Command,
  CommandDialog,
  CommandFooter,
  CommandInput,
  CommandList,
  CommandEmpty,
  CommandGroup,
  CommandItem,
  CommandItemContent,
  CommandItemCrumb,
  CommandItemCrumbSeparator,
  CommandItemDescription,
  CommandItemTitle,
  CommandItemTrail,
  CommandShortcut,
  CommandSeparator,
}
