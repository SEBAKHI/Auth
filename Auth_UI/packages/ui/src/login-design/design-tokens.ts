/**
 * The appearance variables a sign-in page design can use (`var(--primary)`, …) and that
 * its frame inherits by default: a design shows in an isolated document, which sees none
 * of the page's stylesheets, so the page reads each variable's live value and sends it.
 *
 * Read from an element's computed style, so the values are the appearance in force on
 * that element: the platform's, or an application's while its own is switched on, in the
 * current mode. `design-tokens.test.ts` holds this list to the variables preset.css
 * defines, so a token renamed there cannot silently stop reaching designs.
 */
export const DESIGN_TOKENS = [
  "--background",
  "--foreground",
  "--card",
  "--card-foreground",
  "--popover",
  "--popover-foreground",
  "--primary",
  "--primary-foreground",
  "--secondary",
  "--secondary-foreground",
  "--muted",
  "--muted-foreground",
  "--accent",
  "--accent-foreground",
  "--destructive",
  "--border",
  "--input",
  "--ring",
  "--chart-1",
  "--chart-2",
  "--chart-3",
  "--chart-4",
  "--chart-5",
  "--radius",
] as const

export type DesignTokens = Record<string, string>

/**
 * The live values of the design tokens on `element`, plus `--font-sans`: the font the
 * element actually uses. The theme's font variable is inlined into the utilities that use
 * it, so the element's computed font family is the one reliable reading.
 */
export function readDesignTokens(element: Element): DesignTokens {
  const style = getComputedStyle(element)
  const tokens: DesignTokens = {}
  for (const name of DESIGN_TOKENS) {
    const value = style.getPropertyValue(name).trim()
    if (value) tokens[name] = value
  }
  if (style.fontFamily) tokens["--font-sans"] = style.fontFamily
  return tokens
}
