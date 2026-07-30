/**
 * Colour contract for the dashboard.
 *
 * The preset owns colour, and its `--chart-1..5` tokens are a **single-hue cyan
 * lightness ramp** that is identical in light and dark. That is a *sequential*
 * ramp: it encodes magnitude, not identity. Using it for nominal categories
 * double-encodes bar length as hue, and because such lists are ordered by count
 * the colours repaint whenever the data shifts — a reader who learned "the dark
 * one is X" is then misled.
 *
 * So on this dashboard **colour never carries identity**. Nominal comparisons are
 * sorted bars in one hue, where position carries the ranking; part-to-whole over
 * *ordered* classes may use the ramp, because there the ramp's own order is the
 * data's order. There is deliberately no categorical palette here, and no
 * `PALETTE` export to cycle.
 */
export const SERIES = {
  /**
   * Single-measure bars, lines and areas. The mid step: `--chart-1` is too light
   * against the light surface and `--chart-5` too dark against the dark card, so
   * the extremes of the ramp are avoided for the primary mark.
   */
  primary: "var(--chart-2)",

  /**
   * Failed / rejected outcomes. Success-versus-failure is a **status** job, not a
   * categorical one, so it uses the preset's own semantic `--destructive` rather
   * than another step of the ramp — where failure previously read as merely "more
   * of the same measure" instead of something wrong.
   */
  failure: "var(--destructive)",
} as const

/**
 * Ordered ramp for genuinely ordinal classes only — funnel stages, dormancy
 * bands, an account-status share bar. Never for nominal categories.
 */
export const ORDINAL = [
  "var(--chart-2)",
  "var(--chart-3)",
  "var(--chart-4)",
  "var(--chart-5)",
] as const

/** Muted step for the de-emphasised remainder of a share bar. */
export const REMAINDER = "var(--muted)"
