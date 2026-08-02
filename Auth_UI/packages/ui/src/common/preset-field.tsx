import * as React from "react"
import { useTranslation } from "react-i18next"

import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"
import { cn } from "@authsystem/ui/utils"

/** Radix ToggleGroup needs a non-empty item value, and a preset may write "". */
const CUSTOM_KEY = "__custom__"
const presetKey = (index: number) => `p${index}`

export interface Preset {
  /**
   * The value written to the field. Use `""` for the "none" choice of the set
   * (Never / Unlimited / Off) — the caller's schema decides what `""` coerces to.
   */
  value: string
  label: string
}

/**
 * A settable field offered as a short list of sensible choices plus a `Custom`
 * escape hatch, instead of an empty box the user has to fill in.
 *
 * The component is *value*-controlled, not choice-controlled: it holds the same
 * wire value the field always held (e.g. `"60"` minutes, `"production"`), and
 * derives which chip is active by matching that value against `presets`. A value
 * matching no preset selects `Custom` automatically — so an existing record that
 * was saved with an arbitrary number opens on the custom control with its value
 * intact, and the field drops into a react-hook-form `FormField` unchanged.
 *
 * `children` is the custom control, rendered only while `Custom` is active. Pass
 * a render function to receive the current value and setter.
 */
export function PresetField({
  presets,
  value,
  onChange,
  customLabel,
  children,
  className,
  disabled,
  "aria-labelledby": ariaLabelledBy,
}: {
  presets: Preset[]
  value: string
  onChange: (value: string) => void
  customLabel?: string
  children?:
    | React.ReactNode
    | ((args: {
        value: string
        onChange: (value: string) => void
      }) => React.ReactNode)
  className?: string
  disabled?: boolean
  "aria-labelledby"?: string
}) {
  const { t } = useTranslation()

  const matchedIndex = presets.findIndex((preset) => preset.value === value)
  // Sticky: picking Custom must survive the moment before the user has typed a
  // value that no longer matches a preset, otherwise the chip snaps back.
  const [customPinned, setCustomPinned] = React.useState(false)
  const isCustom = customPinned || matchedIndex === -1

  const selected = isCustom ? CUSTOM_KEY : presetKey(matchedIndex)

  return (
    <div className={cn("flex flex-col gap-2", className)}>
      <ToggleGroup
        type="single"
        spacing={2}
        variant="outline"
        value={selected}
        aria-labelledby={ariaLabelledBy}
        disabled={disabled}
        onValueChange={(next) => {
          // Radix allows deselecting the active item; a field must keep a value.
          if (!next) return
          if (next === CUSTOM_KEY) {
            setCustomPinned(true)
            return
          }
          setCustomPinned(false)
          const preset = presets[Number(next.slice(1))]
          if (preset) onChange(preset.value)
        }}
        className="flex-wrap"
      >
        {presets.map((preset, index) => (
          <ToggleGroupItem key={preset.label} value={presetKey(index)}>
            {preset.label}
          </ToggleGroupItem>
        ))}
        {children ? (
          <ToggleGroupItem value={CUSTOM_KEY}>
            {customLabel ?? t("common.custom")}
          </ToggleGroupItem>
        ) : null}
      </ToggleGroup>
      {isCustom && children
        ? typeof children === "function"
          ? children({ value, onChange })
          : children
        : null}
    </div>
  )
}
