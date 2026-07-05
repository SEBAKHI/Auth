/**
 * Fixed color assignments for the dashboard, drawn from the preset chart
 * tokens (a cyan lightness ramp, preset `b1tel7QNE`). Colors follow the
 * entity across every chart: success is always the mid step, failure always
 * the darkest, so the two outcome series stay identifiable page-wide. Never
 * cycle these — fold extra categories into "Other" instead.
 */
export const SERIES = {
  /** Successful outcomes in every success/failure split. */
  success: "var(--chart-2)",
  /** Failed outcomes in every success/failure split. */
  failure: "var(--chart-5)",
  /** Single-measure bars and lines. */
  primary: "var(--chart-2)",
  /** Secondary single-measure charts shown near a primary one. */
  secondary: "var(--chart-3)",
  /** Recessive area fills (audit events backdrop). */
  area: "var(--chart-1)",
} as const

/** Fixed categorical order for donut slices; callers cap slices at its length. */
export const PALETTE = [
  "var(--chart-1)",
  "var(--chart-2)",
  "var(--chart-3)",
  "var(--chart-4)",
  "var(--chart-5)",
] as const
