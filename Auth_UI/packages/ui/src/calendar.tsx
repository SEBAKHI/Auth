import * as React from "react"
import {
  ChevronDownIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
} from "lucide-react"
import { DayButton, DayPicker, getDefaultClassNames } from "react-day-picker"

import { activeDateLocale } from "@astoom/ui/format"
import { cn } from "@astoom/ui/utils"
import { Button, buttonVariants } from "@astoom/ui/button"

/**
 * Calendar built on react-day-picker, styled with the project's preset tokens
 * (no bespoke styling). Defaults from `getDefaultClassNames()` are merged so the
 * layout stays intact even where a class is not overridden.
 */
function Calendar({
  className,
  classNames,
  showOutsideDays = true,
  captionLayout = "label",
  buttonVariant = "ghost",
  formatters,
  components,
  locale = activeDateLocale(),
  ...props
}: React.ComponentProps<typeof DayPicker> & {
  buttonVariant?: React.ComponentProps<typeof Button>["variant"]
}) {
  const defaultClassNames = getDefaultClassNames()

  return (
    <DayPicker
      showOutsideDays={showOutsideDays}
      locale={locale}
      className={cn(
        "group/calendar p-3 [--cell-size:--spacing(8)] [[data-slot=popover-content]_&]:bg-transparent",
        className
      )}
      captionLayout={captionLayout}
      formatters={formatters}
      classNames={{
        root: cn("w-fit", defaultClassNames.root),
        months: cn(
          "relative flex flex-col gap-4 md:flex-row",
          defaultClassNames.months
        ),
        month: cn("flex w-full flex-col gap-4", defaultClassNames.month),
        nav: cn(
          "absolute inset-x-0 top-0 flex w-full items-center justify-between gap-1",
          defaultClassNames.nav
        ),
        button_previous: cn(
          buttonVariants({ variant: buttonVariant }),
          "size-(--cell-size) p-0 select-none",
          defaultClassNames.button_previous
        ),
        button_next: cn(
          buttonVariants({ variant: buttonVariant }),
          "size-(--cell-size) p-0 select-none",
          defaultClassNames.button_next
        ),
        month_caption: cn(
          "flex h-(--cell-size) w-full items-center justify-center px-(--cell-size)",
          defaultClassNames.month_caption
        ),
        dropdowns: cn(
          "flex h-(--cell-size) w-full items-center justify-center gap-1.5 text-sm font-medium",
          defaultClassNames.dropdowns
        ),
        dropdown_root: cn(
          "relative rounded-md border border-input shadow-xs has-focus:border-ring has-focus:ring-3 has-focus:ring-ring/30",
          defaultClassNames.dropdown_root
        ),
        // The real <select> sits invisible over the styled trigger, but its option
        // list is drawn by the browser. `color-scheme` on the root (preset.css)
        // makes that popup follow the theme; these colours are the fallback for
        // platforms that ignore it — without them the month and year lists were
        // light-on-white in dark mode.
        dropdown: cn(
          "absolute inset-0 bg-popover text-popover-foreground opacity-0 [&>option]:bg-popover [&>option]:text-popover-foreground",
          defaultClassNames.dropdown
        ),
        caption_label: cn(
          "select-none font-medium",
          captionLayout === "label"
            ? "text-sm"
            : "flex h-8 items-center gap-1 rounded-md ps-2 pe-1 text-sm [&>svg]:size-3.5 [&>svg]:text-muted-foreground",
          defaultClassNames.caption_label
        ),
        month_grid: cn("w-full border-collapse", defaultClassNames.month_grid),
        weekdays: cn("flex", defaultClassNames.weekdays),
        weekday: cn(
          "flex-1 select-none rounded-md text-[0.8rem] font-normal text-muted-foreground",
          defaultClassNames.weekday
        ),
        week: cn("mt-2 flex w-full", defaultClassNames.week),
        week_number_header: cn(
          "w-(--cell-size) select-none",
          defaultClassNames.week_number_header
        ),
        week_number: cn(
          "select-none text-[0.8rem] text-muted-foreground",
          defaultClassNames.week_number
        ),
        day: cn(
          "group/day relative aspect-square h-full w-full select-none p-0 text-center [&:first-child[data-selected=true]_button]:rounded-s-md [&:last-child[data-selected=true]_button]:rounded-e-md",
          defaultClassNames.day
        ),
        range_start: cn(
          "rounded-s-md bg-accent",
          defaultClassNames.range_start
        ),
        range_middle: cn("rounded-none", defaultClassNames.range_middle),
        range_end: cn("rounded-e-md bg-accent", defaultClassNames.range_end),
        today: cn(
          "rounded-md bg-accent text-accent-foreground data-[selected=true]:rounded-none",
          defaultClassNames.today
        ),
        outside: cn(
          "text-muted-foreground aria-selected:text-muted-foreground",
          defaultClassNames.outside
        ),
        disabled: cn(
          "text-muted-foreground opacity-50",
          defaultClassNames.disabled
        ),
        hidden: cn("invisible", defaultClassNames.hidden),
        ...classNames,
      }}
      components={{
        Root: ({ className, rootRef, ...props }) => (
          <div
            data-slot="calendar"
            ref={rootRef}
            className={cn(className)}
            {...props}
          />
        ),
        Chevron: ({ className, orientation, ...props }) => {
          if (orientation === "left") {
            return (
              <ChevronLeftIcon
                className={cn("size-4 rtl:rotate-180", className)}
                {...props}
              />
            )
          }
          if (orientation === "right") {
            return (
              <ChevronRightIcon
                className={cn("size-4 rtl:rotate-180", className)}
                {...props}
              />
            )
          }
          return (
            <ChevronDownIcon className={cn("size-4", className)} {...props} />
          )
        },
        DayButton: CalendarDayButton,
        ...components,
      }}
      {...props}
    />
  )
}

function CalendarDayButton({
  className,
  day,
  modifiers,
  ...props
}: React.ComponentProps<typeof DayButton>) {
  const defaultClassNames = getDefaultClassNames()

  const ref = React.useRef<HTMLButtonElement>(null)
  React.useEffect(() => {
    if (modifiers.focused) ref.current?.focus()
  }, [modifiers.focused])

  // An endpoint of the selection (a single pick, or either end of a range).
  const isEndpoint =
    (modifiers.selected && !modifiers.range_middle) ||
    modifiers.range_start ||
    modifiers.range_end

  /*
   * The variant is chosen, not overridden with utility classes.
   *
   * Upstream renders every day as `variant="ghost"` and paints the selected one
   * with `data-[selected]:bg-primary`. That works upstream, but this project's ghost
   * variant carries an extra `dark:hover:bg-muted/50`, and Tailwind emits `dark:`
   * variants in a LATER layer than `data-*` ones — verified in the built stylesheet,
   * offset 97570 versus 74463. So on hover the near-white selected background was
   * replaced by dark grey while the text stayed `primary-foreground` (near-black),
   * and the number went unreadable exactly when the pointer was on it. No
   * `data-[…]:hover:` utility can win that race; adding one does nothing.
   *
   * `default` already is "primary background, primary-foreground text, and a hover
   * that only dims the background" — precisely the selected-day behaviour — so
   * selecting the variant removes the conflict instead of fighting the cascade.
   */
  return (
    <Button
      ref={ref}
      variant={isEndpoint ? "default" : "ghost"}
      size="icon"
      data-day={day.date.toLocaleDateString()}
      data-selected-single={
        modifiers.selected &&
        !modifiers.range_start &&
        !modifiers.range_end &&
        !modifiers.range_middle
      }
      data-range-start={modifiers.range_start}
      data-range-end={modifiers.range_end}
      data-range-middle={modifiers.range_middle}
      className={cn(
        "flex aspect-square size-auto w-full min-w-(--cell-size) flex-col gap-1 rounded-md font-normal leading-none data-[range-end=true]:rounded-e-md data-[range-middle=true]:rounded-none data-[range-start=true]:rounded-s-md group-data-[focused=true]/day:border-ring group-data-[focused=true]/day:ring-3 group-data-[focused=true]/day:ring-ring/30",
        // The middle of a range stays ghost-based; `accent-foreground` is light in
        // dark mode, so it survives the ghost hover background legibly.
        "data-[range-middle=true]:bg-accent data-[range-middle=true]:text-accent-foreground",
        defaultClassNames.day,
        className
      )}
      {...props}
    />
  )
}

export { Calendar, CalendarDayButton }
