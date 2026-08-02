import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { Field, FieldDescription, FieldLabel } from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@authsystem/ui/popover"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"

import {
  clampDays,
  DAY_PRESETS,
  MAX_DAYS,
  MIN_DAYS,
  type Granularity,
} from "./use-dashboard-window"

const CUSTOM = "custom"

/**
 * The dashboard's one filter row.
 *
 * It sits above the tabs and scopes every card, rather than each chart carrying
 * its own control: the page used to hide a Daily/Weekly toggle inside two
 * separate chart cards while offering no way at all to change the period.
 *
 * The period is offered as presets first. "Custom" is a day count because that is
 * what the API takes, and it is clamped to the server's own 1..90 rule so the
 * control cannot ask for something that would come back a 400.
 */
export function WindowFilter({
  days,
  granularity,
  onChange,
}: {
  days: number
  granularity: Granularity
  onChange: (next: { days?: number; granularity?: Granularity }) => void
}) {
  const { t } = useTranslation()
  const [open, setOpen] = React.useState(false)
  const [draft, setDraft] = React.useState(String(days))

  const isPreset = (DAY_PRESETS as readonly number[]).includes(days)

  // Seed the draft when the popover opens rather than syncing it from an effect,
  // so reopening always shows the window actually in force and not a stale draft.
  const openCustom = React.useCallback(() => {
    setDraft(String(days))
    setOpen(true)
  }, [days])

  const applyCustom = () => {
    onChange({ days: clampDays(Number(draft)) })
    setOpen(false)
  }

  return (
    <div className="flex flex-wrap items-start gap-x-10 gap-y-5">
      {/* Each group is labelled and explained. They compose — one sets how far
          back to look, the other how the points in that span are bucketed — and
          side by side without labels that was not guessable. */}
      <FilterGroup
        id="dashboard-window"
        label={t("dashboard.window")}
        hint={t("dashboard.windowHint")}
      >
        <ToggleGroup
          type="single"
          spacing={0}
          variant="outline"
          value={isPreset ? String(days) : CUSTOM}
          aria-labelledby="dashboard-window-label"
          onValueChange={(next) => {
            if (!next) return
            if (next === CUSTOM) {
              openCustom()
              return
            }
            onChange({ days: Number(next) })
          }}
        >
          {DAY_PRESETS.map((preset) => (
            <ToggleGroupItem key={preset} value={String(preset)}>
              {t("common.daysShort", { count: preset })}
            </ToggleGroupItem>
          ))}
          <Popover
            open={open}
            onOpenChange={(next) => (next ? openCustom() : setOpen(false))}
          >
            <PopoverTrigger asChild>
              <ToggleGroupItem value={CUSTOM}>
                {isPreset
                  ? t("common.custom")
                  : t("common.daysShort", { count: days })}
              </ToggleGroupItem>
            </PopoverTrigger>
            <PopoverContent align="start" className="w-64">
              <Field>
                <FieldLabel htmlFor="dashboard-window-days">
                  {t("dashboard.windowCustom")}
                </FieldLabel>
                <div className="flex gap-2">
                  <Input
                    id="dashboard-window-days"
                    type="number"
                    inputMode="numeric"
                    min={MIN_DAYS}
                    max={MAX_DAYS}
                    value={draft}
                    onChange={(event) => setDraft(event.target.value)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter") {
                        event.preventDefault()
                        applyCustom()
                      }
                    }}
                  />
                  <Button type="button" onClick={applyCustom}>
                    {t("common.apply")}
                  </Button>
                </div>
                <FieldDescription>
                  {t("dashboard.windowCustomHint", {
                    min: MIN_DAYS,
                    max: MAX_DAYS,
                  })}
                </FieldDescription>
              </Field>
            </PopoverContent>
          </Popover>
        </ToggleGroup>
      </FilterGroup>

      <FilterGroup
        id="dashboard-granularity"
        label={t("dashboard.granularity")}
        hint={t("dashboard.granularityHint")}
      >
        <ToggleGroup
          type="single"
          spacing={0}
          variant="outline"
          value={granularity}
          aria-labelledby="dashboard-granularity-label"
          onValueChange={(next) => {
            if (next === "daily" || next === "weekly") {
              onChange({ granularity: next })
            }
          }}
        >
          <ToggleGroupItem value="daily">
            {t("dashboard.daily")}
          </ToggleGroupItem>
          <ToggleGroupItem value="weekly">
            {t("dashboard.weekly")}
          </ToggleGroupItem>
        </ToggleGroup>
      </FilterGroup>
    </div>
  )
}

/** A labelled, explained control group in the filter bar. */
function FilterGroup({
  id,
  label,
  hint,
  children,
}: {
  id: string
  label: string
  hint: string
  children: React.ReactNode
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <span
        id={`${id}-label`}
        className="text-xs font-medium text-muted-foreground"
      >
        {label}
      </span>
      {children}
      <span className="max-w-xs text-xs text-muted-foreground">{hint}</span>
    </div>
  )
}
