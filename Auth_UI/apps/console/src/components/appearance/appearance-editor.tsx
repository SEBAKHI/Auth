import { MoreHorizontalIcon } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldSeparator,
  FieldTitle,
} from "@authsystem/ui/field"
import { useAdoptedStyleSheet } from "@authsystem/ui/hooks/use-adopted-style-sheet"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupText,
} from "@authsystem/ui/input-group"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { Table, TableBody, TableCell, TableRow } from "@authsystem/ui/table"
import { useTheme } from "@authsystem/ui/theme-provider"
import {
  ACCENT_COLOR_NAMES,
  BASE_COLOR_NAMES,
  CONTROL_HEIGHT,
  CUSTOM_PRESET,
  MENU_ACCENT_OPTIONS,
  RADIUS_OPTIONS,
  SIZE_STEPS,
  baseReadability,
  buildThemeVars,
  currentColorHex,
  registryTitle,
  sizePx,
  swatchColor,
  themeCss,
  type ColorChoice,
  type ColorMode,
  type MenuAccent,
  type RadiusName,
  type SizeChoice,
  type ThemeConfig,
} from "@authsystem/ui/theme/build-theme"
import { themeConfigKey, toThemeConfig } from "@authsystem/ui/theme/theme-config"
import { isHexColor } from "@authsystem/ui/theme/oklch"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"
import { cn } from "@authsystem/ui/utils"

import { SCALES, sizeProblem, type SizeSlot } from "./appearance-rules"

/*
 * The appearance editor: the choices of shadcn/ui's theme builder and the two
 * heights, with a preview of both modes. One editor for every appearance the
 * console edits, the platform's (Platform settings › Appearance) and an
 * application's (an application › Appearance); the cards around it own what
 * differs: where it is saved, and what the preview paints with.
 */

type Role = "base" | "theme" | "chart"

const isBaseName = (name: string) =>
  (BASE_COLOR_NAMES as readonly string[]).includes(name)

/** A colour dot next to a picker entry. The colour is data, not styling. */
function Swatch({ color }: { color: string | undefined }) {
  return (
    <span
      aria-hidden
      className="size-3 shrink-0 rounded-full border"
      style={{ backgroundColor: color }}
    />
  )
}

/**
 * One mode's custom colour as a compact chip: the browser's colour picker as
 * the swatch, the code right beside it (for pasting a brand colour exactly),
 * and the mode it belongs to. Two of these sit side by side, so a code is
 * never read far from its colour.
 */
function ColorChip({
  id,
  mode,
  value,
  onChange,
}: {
  id: string
  mode: ColorMode
  value: string
  onChange: (hex: string) => void
}) {
  const { t } = useTranslation()
  const label = t(mode === "light" ? "platformSettings.logoLight" : "platformSettings.logoDark")
  const [text, setText] = React.useState(value)
  // The text follows the picker, but never overwrites a code being typed.
  const [lastValue, setLastValue] = React.useState(value)
  if (value !== lastValue) {
    setLastValue(value)
    setText(value)
  }
  const invalid = !isHexColor(text)

  return (
    <InputGroup className="w-auto">
      <InputGroupAddon>
        <input
          type="color"
          aria-label={label}
          value={value}
          onChange={(event) => onChange(event.target.value.toLowerCase())}
          className="size-5 cursor-pointer rounded-full border-0 bg-transparent p-0"
        />
      </InputGroupAddon>
      <InputGroupInput
        id={id}
        aria-label={label}
        // A colour code reads left to right in every language, like the URL
        // fields of the application form.
        dir="ltr"
        value={text}
        maxLength={7}
        spellCheck={false}
        aria-invalid={invalid}
        className="w-[10ch] tabular-nums"
        onChange={(event) => {
          const next = event.target.value.trim()
          setText(next)
          if (isHexColor(next)) onChange(next.toLowerCase())
        }}
      />
      <InputGroupAddon align="inline-end">
        <InputGroupText>{label}</InputGroupText>
      </InputGroupAddon>
    </InputGroup>
  )
}

function ColorChoiceField({
  role,
  value,
  options,
  config,
  onChange,
}: {
  role: Role
  value: ColorChoice
  options: readonly string[]
  config: ThemeConfig
  onChange: (next: ColorChoice) => void
}) {
  const { t } = useTranslation()
  const { resolvedTheme } = useTheme()
  const id = `appearance-${role}`
  const bases = options.filter(isBaseName)
  const accents = options.filter((name) => !isBaseName(name))

  const choose = (preset: string) => {
    if (preset !== CUSTOM_PRESET) {
      onChange({ preset })
      return
    }
    // Start from what is on screen, so switching to custom changes nothing
    // until a colour is actually picked.
    onChange({
      preset,
      light: currentColorHex(config, role, "light"),
      dark: currentColorHex(config, role, "dark"),
    })
  }

  const setMode = (mode: ColorMode, hex: string) =>
    onChange({ ...value, [mode]: hex })

  const item = (name: string) => (
    <SelectItem key={name} value={name}>
      <Swatch color={swatchColor(name, role, resolvedTheme)} />
      {registryTitle(name)}
    </SelectItem>
  )

  const custom = value.preset === CUSTOM_PRESET

  // A settings row, as System settings draws its rows: what it is on the
  // start side, the control on the end side, stacked when the column is narrow.
  return (
    <Field orientation="responsive">
      <FieldContent>
        <FieldLabel htmlFor={id}>{t(`platformSettings.appearance.${role}`)}</FieldLabel>
        <FieldDescription>
          {custom
            ? t(`platformSettings.appearance.customHints.${role}`)
            : t(`platformSettings.appearance.${role}Hint`)}
        </FieldDescription>
      </FieldContent>
      <div className="flex flex-col gap-2 @md/field-group:items-end">
        <Select value={value.preset} onValueChange={choose}>
          <SelectTrigger id={id} className="w-full @md/field-group:w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {bases.length > 0 ? <SelectGroup>{bases.map(item)}</SelectGroup> : null}
            {bases.length > 0 ? <SelectSeparator /> : null}
            <SelectGroup>{accents.map(item)}</SelectGroup>
            <SelectSeparator />
            <SelectGroup>
              <SelectItem value={CUSTOM_PRESET}>
                {t("platformSettings.appearance.custom")}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        {custom ? (
          <div className="flex flex-wrap gap-2 @md/field-group:justify-end">
            <ColorChip
              id={`${id}-light`}
              mode="light"
              value={value.light ?? "#000000"}
              onChange={(hex) => setMode("light", hex)}
            />
            <ColorChip
              id={`${id}-dark`}
              mode="dark"
              value={value.dark ?? "#000000"}
              onChange={(hex) => setMode("dark", hex)}
            />
          </div>
        ) : null}
        {custom && role === "base" ? <ReadabilityWarnings value={value} /> : null}
      </div>
    </Field>
  )
}

/**
 * One of the two heights: three steps and "custom", which opens a number
 * field. The whole console previews the draft, so the controls on this very
 * card change height as the administrator picks.
 */
function SizeChoiceField({
  slot,
  config,
  onChange,
}: {
  slot: SizeSlot
  config: ThemeConfig
  onChange: (next: SizeChoice) => void
}) {
  const { t } = useTranslation()
  const id = `appearance-${slot}`
  const scale = SCALES[slot]
  const value = config[slot]
  const custom = value.preset === CUSTOM_PRESET
  const problem = sizeProblem(config, slot)

  // The text follows the draft, but never overwrites a number being typed
  // ("3" on the way to "34" is not yet a height).
  const [text, setText] = React.useState(value.px == null ? "" : String(value.px))
  const [lastPx, setLastPx] = React.useState(value.px)
  if (value.px !== lastPx) {
    setLastPx(value.px)
    if (Number(text) !== value.px) setText(value.px == null ? "" : String(value.px))
  }

  const choose = (preset: string) => {
    // An empty value is the group deselecting itself on a second click.
    if (!preset) return
    if (preset !== CUSTOM_PRESET) {
      onChange({ preset: preset as SizeChoice["preset"] })
      return
    }
    // Start from the height on screen, so switching changes nothing until a
    // number is typed.
    onChange({ preset: CUSTOM_PRESET, px: sizePx(value, scale) })
  }

  return (
    <Field data-invalid={problem ? true : undefined}>
      <FieldTitle id={`${id}-label`}>{t(`platformSettings.appearance.${slot}`)}</FieldTitle>
      <ToggleGroup
        type="single"
        spacing={0}
        variant="outline"
        className="flex-wrap"
        value={value.preset}
        aria-labelledby={`${id}-label`}
        onValueChange={choose}
      >
        {[...SIZE_STEPS, CUSTOM_PRESET].map((step) => (
          <ToggleGroupItem key={step} value={step}>
            {t(`platformSettings.appearance.sizes.${step}`)}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
      {custom ? (
        // A Field stretches its own children; the wrapper takes the row so the
        // field can stay as wide as a height.
        <div>
          <InputGroup className="w-fit">
            <InputGroupInput
              id={id}
              type="number"
              inputMode="numeric"
              min={scale.min}
              max={scale.max}
              step={1}
              aria-label={t("platformSettings.appearance.customPx", {
                name: t(`platformSettings.appearance.${slot}`),
              })}
              aria-invalid={problem === "range"}
              className="w-[6ch] tabular-nums"
              value={text}
              onChange={(event) => {
                setText(event.target.value)
                const px = event.target.value.trim() === "" ? null : Number(event.target.value)
                onChange({ preset: CUSTOM_PRESET, px })
              }}
            />
            <InputGroupAddon align="inline-end">
              <InputGroupText>{t("platformSettings.appearance.pxUnit")}</InputGroupText>
            </InputGroupAddon>
          </InputGroup>
        </div>
      ) : null}
      <FieldDescription>
        {t(`platformSettings.appearance.${slot}Hint`, { px: sizePx(value, scale) })}
        {custom && problem !== "range"
          ? ` ${t("platformSettings.appearance.customPxRange", { min: scale.min, max: scale.max })}`
          : null}
      </FieldDescription>
      {problem === "range" ? (
        <FieldError>
          {t("platformSettings.appearance.customPxRange", { min: scale.min, max: scale.max })}
        </FieldError>
      ) : problem === "belowControls" ? (
        <FieldError>
          {t("platformSettings.appearance.rowBelowControls", {
            px: sizePx(config.controlSize, CONTROL_HEIGHT),
          })}
        </FieldError>
      ) : null}
    </Field>
  )
}

/**
 * Both modes at once, built from the real components and painted by the
 * draft: the page itself shows only the mode it is in, and charts appear
 * nowhere on it. `.light` / `.dark` scope the platform's tokens to each tile
 * (preset.css and the runtime stylesheet both honour them). Purely visual,
 * so the tiles are inert and hidden from assistive technology.
 *
 * With a `scope`, the tiles sit inside `[data-appearance-scope]`, where the
 * editor's own stylesheet paints them with the draft alone.
 */
function AppearancePreview({ name, scope }: { name: string; scope?: string }) {
  const { t } = useTranslation()
  const bars = ["h-2/5 bg-chart-1", "h-3/5 bg-chart-2", "h-full bg-chart-3", "h-4/5 bg-chart-4", "h-1/2 bg-chart-5"]

  return (
    <section aria-labelledby="appearance-preview" className="flex flex-col gap-3">
      <FieldTitle id="appearance-preview">{t("platformSettings.appearance.preview")}</FieldTitle>
      <div
        className="grid grid-cols-1 gap-3 sm:grid-cols-2"
        aria-hidden
        inert
        data-appearance-scope={scope}
      >
        {(["light", "dark"] as const).map((mode) => (
          <div
            key={mode}
            data-mode={mode}
            className={cn(
              mode,
              "flex min-w-0 flex-col gap-3 rounded-2xl bg-background p-3 text-foreground ring-1 ring-foreground/10"
            )}
          >
            <p className="text-xs text-muted-foreground">
              {t(mode === "light" ? "platformSettings.logoLight" : "platformSettings.logoDark")}
            </p>
            <div className="flex flex-col gap-1 rounded-xl bg-card p-3 text-card-foreground ring-1 ring-foreground/10">
              <p className="truncate font-medium">{name}</p>
              <p className="text-xs text-muted-foreground">
                {t("platformSettings.appearance.previewText")}
              </p>
            </div>
            <div className="flex gap-2">
              <InputGroup>
                <InputGroupInput placeholder="name@example.com" dir="ltr" />
              </InputGroup>
              <Button>{t("common.save")}</Button>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button size="sm" variant="outline">
                {t("common.cancel")}
              </Button>
              <Badge variant="secondary">{t("common.active")}</Badge>
            </div>
            <Table>
              <TableBody>
                {/* A row of text, then a row as the list pages draw theirs (an
                    avatar and an actions button), which the setting leaves
                    taller by their extra height. The names truncate inside
                    their own direction, so a Latin name in Arabic keeps its start. */}
                <TableRow>
                  <TableCell className="w-full max-w-0">
                    <bdi dir="auto" className="block truncate">
                      {name}
                    </bdi>
                  </TableCell>
                  <TableCell className="text-end">{t("common.active")}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell className="w-full max-w-0">
                    <div className="flex items-center gap-2">
                      <EntityAvatar name={name} />
                      <bdi dir="auto" className="min-w-0 truncate">
                        name@example.com
                      </bdi>
                    </div>
                  </TableCell>
                  <TableCell className="text-end">
                    <Button variant="ghost" size="icon-sm">
                      <MoreHorizontalIcon />
                      <span className="sr-only">{t("common.actions")}</span>
                    </Button>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
            <div className="rounded-lg bg-accent px-3 py-1.5 text-sm text-accent-foreground">
              {t("platformSettings.appearance.previewMenuItem")}
            </div>
            <div className="flex h-12 items-end gap-1">
              {bars.map((bar) => (
                <div key={bar} className={cn("flex-1 rounded-t-sm", bar)} />
              ))}
            </div>
          </div>
        ))}
      </div>
    </section>
  )
}

/**
 * A custom base colour is applied literally, so it can be one no text reads
 * well on. Saying so is the console's job; refusing is not — the colour is
 * the administrator's to choose.
 */
function ReadabilityWarnings({ value }: { value: ColorChoice }) {
  const { t } = useTranslation()
  const modes = (["light", "dark"] as const)
    .map((mode) => ({
      mode,
      ratio: baseReadability((mode === "light" ? value.light : value.dark) ?? "", mode),
    }))
    .filter((entry) => entry.ratio < 4.5)

  if (modes.length === 0) return null

  return (
    <Alert>
      <AlertTitle>{t("platformSettings.appearance.lowContrastTitle")}</AlertTitle>
      {modes.map((entry) => (
        <AlertDescription key={entry.mode}>
          {t("platformSettings.appearance.lowContrast", {
            mode: t(entry.mode === "light" ? "platformSettings.logoLight" : "platformSettings.logoDark"),
            ratio: entry.ratio.toFixed(1),
          })}
        </AlertDescription>
      ))}
    </Alert>
  )
}

/**
 * The appearance after the base colour changed. A monochrome theme or chart
 * colour belongs to its base: on another base it becomes that base's own, and
 * on a custom base (which has no registry entry) it becomes custom, seeded
 * with what was on screen — shadcn's picker does the same.
 */
function withBase(config: ThemeConfig, base: ColorChoice): ThemeConfig {
  const next = { ...config, base }
  const follow = (role: "theme" | "chart"): ColorChoice => {
    const current = config[role]
    if (!isBaseName(current.preset)) return current
    if (base.preset !== CUSTOM_PRESET) return { preset: base.preset }
    return {
      preset: CUSTOM_PRESET,
      light: currentColorHex(config, role, "light"),
      dark: currentColorHex(config, role, "dark"),
    }
  }
  return { ...next, theme: follow("theme"), chart: follow("chart") }
}

/**
 * The preview's own stylesheet: the appearance being edited, for the inside of
 * `[data-appearance-scope="<scope>"]` alone (build-theme.ts `themeCss`).
 */
function ScopedAppearance({ config, scope }: { config: ThemeConfig; scope: string }) {
  // The key is the configuration itself, so a new object with the same
  // choices computes nothing again.
  const key = themeConfigKey(config)
  const css = React.useMemo(
    () => themeCss(buildThemeVars(toThemeConfig(JSON.parse(key))), scope),
    [key, scope]
  )
  useAdoptedStyleSheet(css)
  return null
}

/**
 * Every choice of an appearance, and the preview of both modes.
 *
 * Without `previewScope` the preview's tiles take the page's own tokens: the
 * platform's card previews by recolouring the whole console for its
 * administrator. With it, the tiles alone are painted with `value`, and the
 * console keeps its own colours: an application's appearance is never the
 * console's.
 */
export function AppearanceEditor({
  value,
  onChange,
  previewName,
  previewScope,
}: {
  value: ThemeConfig
  onChange: (update: (current: ThemeConfig) => ThemeConfig) => void
  previewName: string
  previewScope?: string
}) {
  const { t } = useTranslation()
  const baseOptions = [...BASE_COLOR_NAMES]
  const themeOptions = isBaseName(value.base.preset)
    ? [value.base.preset, ...ACCENT_COLOR_NAMES]
    : ACCENT_COLOR_NAMES

  return (
    <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,24rem)]">
      <FieldGroup>
        <ColorChoiceField
          role="base"
          value={value.base}
          options={baseOptions}
          config={value}
          onChange={(base) => onChange((current) => withBase(current, base))}
        />
        <FieldSeparator />
        <ColorChoiceField
          role="theme"
          value={value.theme}
          options={themeOptions}
          config={value}
          onChange={(theme) => onChange((current) => ({ ...current, theme }))}
        />
        <FieldSeparator />
        <ColorChoiceField
          role="chart"
          value={value.chart}
          options={themeOptions}
          config={value}
          onChange={(chart) => onChange((current) => ({ ...current, chart }))}
        />
        <FieldSeparator />
        <div className="grid gap-7 @lg/field-group:grid-cols-2">
          <Field>
            <FieldTitle id="appearance-radius">
              {t("platformSettings.appearance.radius")}
            </FieldTitle>
            <ToggleGroup
              type="single"
              spacing={0}
              variant="outline"
              className="flex-wrap"
              value={value.radius}
              aria-labelledby="appearance-radius"
              onValueChange={(next) => {
                // An empty value is the group deselecting itself on a second click.
                if (!next) return
                onChange((current) => ({ ...current, radius: next as RadiusName }))
              }}
            >
              {RADIUS_OPTIONS.map((option) => (
                <ToggleGroupItem key={option.name} value={option.name}>
                  {t(`platformSettings.appearance.radii.${option.name}`)}
                </ToggleGroupItem>
              ))}
            </ToggleGroup>
          </Field>
          <Field>
            <FieldTitle id="appearance-menu-accent">
              {t("platformSettings.appearance.menuAccent")}
            </FieldTitle>
            <ToggleGroup
              type="single"
              spacing={0}
              variant="outline"
              value={value.menuAccent}
              aria-labelledby="appearance-menu-accent"
              onValueChange={(next) => {
                if (!next) return
                onChange((current) => ({ ...current, menuAccent: next as MenuAccent }))
              }}
            >
              {MENU_ACCENT_OPTIONS.map((option) => (
                <ToggleGroupItem key={option} value={option}>
                  {t(`platformSettings.appearance.menuAccents.${option}`)}
                </ToggleGroupItem>
              ))}
            </ToggleGroup>
            <FieldDescription>
              {t("platformSettings.appearance.menuAccentHint")}
            </FieldDescription>
          </Field>
        </div>
        <FieldSeparator />
        <div className="grid gap-7">
          <SizeChoiceField
            slot="controlSize"
            config={value}
            onChange={(controlSize) => onChange((current) => ({ ...current, controlSize }))}
          />
          <SizeChoiceField
            slot="tableRowHeight"
            config={value}
            onChange={(tableRowHeight) => onChange((current) => ({ ...current, tableRowHeight }))}
          />
        </div>
      </FieldGroup>
      {previewScope ? <ScopedAppearance config={value} scope={previewScope} /> : null}
      <AppearancePreview name={previewName} scope={previewScope} />
    </div>
  )
}
