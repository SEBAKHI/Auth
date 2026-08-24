import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { Tabs as TabsPrimitive } from "radix-ui"

import { cn } from "@authsystem/ui/utils"

function Tabs({
  className,
  orientation = "horizontal",
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Root>) {
  return (
    <TabsPrimitive.Root
      data-slot="tabs"
      data-orientation={orientation}
      className={cn(
        "group/tabs flex gap-2 data-horizontal:flex-col",
        className
      )}
      {...props}
    />
  )
}

const tabsListVariants = cva(
  // The triggers are `whitespace-nowrap` and cannot shrink, so `w-fit` resolves
  // to their combined min-content width — wider than the viewport once a strip
  // carries a few tabs. `max-w-full` + `overflow-x-auto` keeps the strip inside
  // its column and scrolls the tabs instead of widening the document. The
  // scrollbar is hidden because it would eat half of the 36px-tall pill; the
  // padding leaves room for the focus ring and the line-variant underline so
  // neither is clipped by the scroll container.
  // The block padding is 3.6px against 4px inline, deliberately: the pill's
  // rounded ends pull its top and bottom edges optically inward, so a
  // geometrically equal 4px inset reads as a looser gap above and below than
  // beside. Trimming the block side by a subpixel is the correction. The
  // trigger is `h-full`, so this is also what sets the pill's height.
  // `justify-center-safe` rather than `justify-center`: centring an overflowing
  // flex line splits the overflow across both sides, and the leading half can
  // never be scrolled back into view. `safe` falls back to start alignment
  // exactly when the tabs overflow, which is the only time it can matter here.
  "group/tabs-list inline-flex w-fit items-center justify-center-safe rounded-full px-1 py-[3.6px] text-muted-foreground group-data-horizontal/tabs:h-9 group-data-horizontal/tabs:max-w-full group-data-horizontal/tabs:overflow-x-auto group-data-vertical/tabs:h-fit group-data-vertical/tabs:flex-col group-data-vertical/tabs:rounded-2xl data-[variant=line]:rounded-none [scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
  {
    variants: {
      variant: {
        default: "bg-muted",
        line: "gap-1 bg-transparent",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

function TabsList({
  className,
  variant = "default",
  ...props
}: React.ComponentProps<typeof TabsPrimitive.List> &
  VariantProps<typeof tabsListVariants>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      data-variant={variant}
      className={cn(tabsListVariants({ variant }), className)}
      {...props}
    />
  )
}

/**
 * The trigger's appearance, lifted out of the component so a strip of route
 * links can look identical without pretending to be a tablist. Radix marks the
 * selected trigger with `data-state="active"`; a link marks itself with
 * `data-active`, and this build's `data-active:` variant matches either.
 *
 * `h-full`, not upstream's `h-[calc(100%-1px)]`: the trigger fills the list's
 * content box exactly, so the gap above and below the pill is the list's own
 * block padding and nothing else — the height is owned in one place. The odd
 * pixel the upstream value left over put half of itself on each side, which is
 * a gap no one chose and one that cannot be tuned from the list.
 */
const tabsTriggerVariants = cva([
  "relative inline-flex h-full flex-1 items-center justify-center gap-2 rounded-full border border-transparent! px-3 py-1 text-sm font-medium whitespace-nowrap text-foreground/60 transition-all group-data-vertical/tabs:w-full group-data-vertical/tabs:justify-start group-data-vertical/tabs:rounded-2xl group-data-vertical/tabs:px-3 group-data-vertical/tabs:py-1.5 hover:text-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-1 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50 has-data-[icon=inline-end]:pe-2 has-data-[icon=inline-start]:ps-2 dark:text-muted-foreground dark:hover:text-foreground [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  "group-data-[variant=line]/tabs-list:bg-transparent group-data-[variant=line]/tabs-list:data-active:bg-transparent dark:group-data-[variant=line]/tabs-list:data-active:border-transparent dark:group-data-[variant=line]/tabs-list:data-active:bg-transparent",
  "data-active:bg-background data-active:text-foreground dark:data-active:border-input dark:data-active:bg-input/30 dark:data-active:text-foreground",
  "after:absolute after:bg-foreground after:opacity-0 after:transition-opacity group-data-horizontal/tabs:after:inset-x-0 group-data-horizontal/tabs:after:bottom-[-5px] group-data-horizontal/tabs:after:h-0.5 group-data-vertical/tabs:after:inset-y-0 group-data-vertical/tabs:after:-end-1 group-data-vertical/tabs:after:w-0.5 group-data-[variant=line]/tabs-list:data-active:after:opacity-100",
])

function TabsTrigger({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      data-slot="tabs-trigger"
      className={cn(tabsTriggerVariants(), className)}
      {...props}
    />
  )
}

function TabsContent({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Content>) {
  return (
    <TabsPrimitive.Content
      data-slot="tabs-content"
      className={cn("flex-1 text-sm outline-none", className)}
      {...props}
    />
  )
}

export {
  Tabs,
  TabsList,
  TabsTrigger,
  TabsContent,
  tabsListVariants,
  tabsTriggerVariants,
}
