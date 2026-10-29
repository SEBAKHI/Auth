import {
  CONTROL_HEIGHT,
  CUSTOM_PRESET,
  TABLE_ROW_HEIGHT,
  isValidCustomPx,
  sizePx,
  type SizeScale,
  type ThemeConfig,
} from "@authsystem/ui/theme/theme-config"

/** The two heights of an appearance. */
export type SizeSlot = "controlSize" | "tableRowHeight"

export const SCALES: Record<SizeSlot, SizeScale> = {
  controlSize: CONTROL_HEIGHT,
  tableRowHeight: TABLE_ROW_HEIGHT,
}

/**
 * What keeps a size from being saved, the same rules the API applies: a
 * custom height in whole pixels within its bounds, and rows no lower than
 * the controls they hold.
 */
export function sizeProblem(config: ThemeConfig, slot: SizeSlot): "range" | "belowControls" | null {
  const value = config[slot]
  if (value.preset === CUSTOM_PRESET && !isValidCustomPx(value.px, SCALES[slot])) return "range"
  if (
    slot === "tableRowHeight" &&
    sizeProblem(config, "controlSize") === null &&
    sizePx(config.tableRowHeight, TABLE_ROW_HEIGHT) < sizePx(config.controlSize, CONTROL_HEIGHT)
  ) {
    return "belowControls"
  }
  return null
}

/** Whether an appearance can be saved: its heights follow the API's rules. */
export function isAppearanceSavable(config: ThemeConfig): boolean {
  return sizeProblem(config, "controlSize") === null && sizeProblem(config, "tableRowHeight") === null
}
