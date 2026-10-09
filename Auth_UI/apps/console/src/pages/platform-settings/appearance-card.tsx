import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Badge } from "@authsystem/ui/badge"
import { BRANDING_QUERY_KEY, useThemePreview } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  FieldSeparator,
  FieldTitle,
} from "@authsystem/ui/field"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"
import { Input } from "@authsystem/ui/input"
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
import { Spinner } from "@authsystem/ui/spinner"
import { useTheme } from "@authsystem/ui/theme-provider"
import {
  ACCENT_COLOR_NAMES,
  BASE_COLOR_NAMES,
  CUSTOM_PRESET,
  DEFAULT_THEME_CONFIG,
  MENU_ACCENT_OPTIONS,
  RADIUS_OPTIONS,
  baseReadability,
  currentColorHex,
  registryTitle,
  swatchColor,
  type ColorChoice,
  type ColorMode,
  type MenuAccent,
  type RadiusName,
  type ThemeConfig,
} from "@authsystem/ui/theme/build-theme"
import {
  themeConfigKey,
  toThemeConfig,
  toThemeRequest,
} from "@authsystem/ui/theme/theme-config"
import { isHexColor } from "@authsystem/ui/theme/oklch"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"
import { cn } from "@authsystem/ui/utils"

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
 * Both modes at once, built from the real components and painted by the
 * draft: the page itself shows only the mode it is in, and charts appear
 * nowhere on it. `.light` / `.dark` scope the platform's tokens to each tile
 * (preset.css and the runtime stylesheet both honour them). Purely visual,
 * so the tiles are inert and hidden from assistive technology.
 */
function AppearancePreview({ name }: { name: string }) {
  const { t } = useTranslation()
  const bars = ["h-2/5 bg-chart-1", "h-3/5 bg-chart-2", "h-full bg-chart-3", "h-4/5 bg-chart-4", "h-1/2 bg-chart-5"]

  return (
    <section aria-labelledby="appearance-preview" className="flex flex-col gap-3">
      <FieldTitle id="appearance-preview">{t("platformSettings.appearance.preview")}</FieldTitle>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2" aria-hidden inert>
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
            <div className="flex flex-wrap gap-2">
              <Button size="sm">{t("common.save")}</Button>
              <Button size="sm" variant="outline">
                {t("common.cancel")}
              </Button>
              <Badge variant="secondary">{t("common.active")}</Badge>
            </div>
            <Input placeholder="name@example.com" dir="ltr" />
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

export function AppearanceCard({
  settings,
  onSaved,
}: {
  settings: Schemas["PlatformSettingsDto"]
  onSaved: (saved: Schemas["PlatformSettingsDto"] | undefined) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { setThemePreview } = useThemePreview()

  // Seeded once, at mount: a refetch must not overwrite what is being edited.
  const [saved, setSaved] = React.useState(() => toThemeConfig(settings.theme))
  const [draft, setDraft] = React.useState(saved)
  const isDirty = themeConfigKey(draft) !== themeConfigKey(saved)

  // The whole console previews the draft for this administrator only.
  React.useEffect(() => {
    setThemePreview(isDirty ? draft : null)
  }, [draft, isDirty, setThemePreview])
  React.useEffect(() => () => setThemePreview(null), [setThemePreview])

  const save = useMutation({
    mutationFn: async (config: ThemeConfig) => {
      const { data, error } = await api.PUT("/api/v1/admin/platform-settings/theme", {
        body: toThemeRequest(config),
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      onSaved(data)
      const next = toThemeConfig(data?.theme)
      // The branding read carries the appearance every visitor gets. Writing
      // the saved one into it before the preview is dropped keeps the old
      // colours from flashing back while the refetch runs.
      queryClient.setQueryData<Schemas["PlatformBrandingDto"]>(
        BRANDING_QUERY_KEY,
        (old) => (old ? { ...old, theme: data?.theme ?? old.theme } : old)
      )
      void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
      setSaved(next)
      setDraft(next)
      toast.success(t("platformSettings.updated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const unsavedPrompt = useUnsavedChangesPrompt({
    isDirty,
    isSaving: save.isPending,
  })

  const baseOptions = [...BASE_COLOR_NAMES]
  const themeOptions = isBaseName(draft.base.preset)
    ? [draft.base.preset, ...ACCENT_COLOR_NAMES]
    : ACCENT_COLOR_NAMES
  const isDefault = themeConfigKey(draft) === themeConfigKey(DEFAULT_THEME_CONFIG)

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("platformSettings.appearance.title")}</CardTitle>
        <CardDescription>{t("platformSettings.appearance.subtitle")}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,24rem)]">
          <FieldGroup>
            <ColorChoiceField
              role="base"
              value={draft.base}
              options={baseOptions}
              config={draft}
              onChange={(base) => setDraft((current) => withBase(current, base))}
            />
            <FieldSeparator />
            <ColorChoiceField
              role="theme"
              value={draft.theme}
              options={themeOptions}
              config={draft}
              onChange={(theme) => setDraft((current) => ({ ...current, theme }))}
            />
            <FieldSeparator />
            <ColorChoiceField
              role="chart"
              value={draft.chart}
              options={themeOptions}
              config={draft}
              onChange={(chart) => setDraft((current) => ({ ...current, chart }))}
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
                  value={draft.radius}
                  aria-labelledby="appearance-radius"
                  onValueChange={(next) => {
                    // An empty value is the group deselecting itself on a second click.
                    if (!next) return
                    setDraft((current) => ({ ...current, radius: next as RadiusName }))
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
                  value={draft.menuAccent}
                  aria-labelledby="appearance-menu-accent"
                  onValueChange={(next) => {
                    if (!next) return
                    setDraft((current) => ({ ...current, menuAccent: next as MenuAccent }))
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
          </FieldGroup>
          <AppearancePreview name={settings.platformName ?? ""} />
        </div>
      </CardContent>
      <CardFooter className="flex-wrap gap-2">
        <Button
          disabled={!isDirty || save.isPending}
          onClick={() => save.mutate(draft)}
        >
          {save.isPending ? <Spinner data-icon="inline-start" /> : null}
          {t("common.save")}
        </Button>
        <Button
          variant="outline"
          disabled={!isDirty || save.isPending}
          onClick={() => setDraft(saved)}
        >
          {t("platformSettings.appearance.revert")}
        </Button>
        <Button
          variant="ghost"
          disabled={isDefault || save.isPending}
          onClick={() => setDraft(DEFAULT_THEME_CONFIG)}
        >
          {t("platformSettings.appearance.resetToDefault")}
        </Button>
      </CardFooter>
      {unsavedPrompt}
    </Card>
  )
}
