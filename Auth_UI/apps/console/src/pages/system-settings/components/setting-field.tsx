import type * as React from "react"
import { Link } from "react-router-dom"
import { useTranslation } from "react-i18next"
import type { Control, FieldValues } from "react-hook-form"

import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import { FieldConstraints } from "@authsystem/ui/common/field-constraints"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldLabel,
} from "@authsystem/ui/field"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { Switch } from "@authsystem/ui/switch"
import { Textarea } from "@authsystem/ui/textarea"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"

import {
  SECTION_COMPANION_PAGES,
  fieldI18nKey,
  formFieldName,
  settingAnchorId,
  valueDirection,
  type SystemSettingsField,
} from "../lib/sections"
import {
  isolateFirstStrong,
  renderSettingValue,
  useValueLabels,
} from "../lib/setting-value"

/** Read from the registry so the card button and these rows can never diverge. */
const SECRET_KEYS_ROUTE = SECTION_COMPANION_PAGES.SecretManagement.route

/**
 * Row geometry, applied to every setting alike (Windows 11 SettingsCard /
 * macOS System Settings): the row spans the card, the label and its hint read
 * at the start, and the control is pinned to the END of the row at a width
 * that suits its value. That way a wide card is used by the layout instead of
 * by a stretched input, and the controls line up as one column no matter how
 * wide the window gets.
 *
 * On a large monitor that puts real distance between a label and its control,
 * so each row is ruled off from the next: the line pairs the two ends of a
 * row while simply reading down the page, and the hover tint (plus
 * focus-within, so the aid is not mouse-only) confirms the one in play. The
 * rule and the tint reuse the tokens the separator and the data tables
 * already use.
 *
 * The row bleeds into the card padding rather than indenting the content:
 * that needs the explicit width, because Field is `w-full` and a negative
 * margin alone would slide the row sideways instead of widening it.
 */
/**
 * The arrival highlight is a ring rather than a tint. The palette has exactly
 * one step between the card and a tinted surface — `muted` is 3% darker than
 * `card` in light mode — which is enough for a hover cue the pointer already
 * points at, and not enough for "here is the row you searched for" on a page
 * of forty. It also has to land on the category header, which is tinted
 * already and where a tint would have nothing left to say.
 */
/**
 * The unsaved rail: a 2px bar on the row's inline-start edge while the field
 * differs from the values the form opened with.
 *
 * Drawn on the state, and PAID FOR out of the padding beside it. A border lives
 * in the box model, so a 2px rail takes 2px out of what a border-box row has to
 * give its content — and this row's geometry is measured rather than
 * incidental: `settings-measure` asserts a hint of exactly 668px and a switch
 * exactly 124px from the end of it, numbers `index.css` derives in a comment.
 * Two ways to draw the rail both move those numbers, in opposite directions:
 * reserving it on every row (transparent when clean) narrows all forty rows for
 * good, and adding it only when the row turns dirty jolts the label and the
 * control 2px sideways under the operator's own first keystroke.
 *
 * So the rail is added AND the inline-start padding gives back exactly what it
 * takes: 12 → 10 on a card row (`px-3` → `ps-2.5`), 16 → 14 inside a panel
 * (`px-4` → `ps-3.5`). A clean row is untouched, a dirty row's content sits
 * where it always sat, and the mark costs the layout nothing in either state.
 * The data-variant beats the plain `px-*` on specificity, so order in the class
 * string is not load-bearing.
 *
 * `border-s-2` and `border-s-foreground` were both compiled against the
 * installed Tailwind (4.3.1) before being written here — `border-inline-start-
 * width: 2px` and `border-inline-start-color` respectively — because a logical
 * per-side border COLOUR is the half of the pair that is easy to assume and not
 * have. The colour is explicit because this repo sets a default border colour,
 * and an uncoloured rail would inherit `border-border`: the same near-invisible
 * hairline the row already draws underneath itself.
 *
 * `foreground`, not `primary`: `command` already marks the row-in-a-state with
 * a `bg-foreground` bar at the inline start, and the unsaved LINE below the
 * label is a `text-foreground` step — one state, one ink. `primary` in this
 * console is the button fill, which reads as "act on me" rather than "you
 * changed me". The palette is achromatic, so this is a luminance mark in both
 * themes: 0.145 on a 1.0 card in light, 0.985 on a 0.205 card in dark.
 *
 * On `categoryHeader` the rail is kept as it is, with no special case. That row
 * is `rounded-t-xl` and has no top border for the rail to meet, so the radius
 * renders it as a 2px edge tapering to nothing as it climbs into the corner,
 * where it crosses the panel's own 1px border. It does not cut square across
 * the rounded corner, which was the thing worth checking. Special-casing it
 * would mean a second visual language for one state — the thing this mark
 * exists to avoid.
 */
const ROW_BASE =
  "justify-between transition-colors hover:bg-muted/50 focus-within:bg-muted/50 data-[dirty=true]:border-s-2 data-[dirty=true]:border-s-foreground data-[highlight]:ring-2 data-[highlight]:ring-ring data-[highlight]:ring-inset"

/**
 * Where the row sits, which is the only thing that changes about it.
 *
 * `card` is the row directly in the section card, bleeding into the card's own
 * padding so the rule spans it. `category` is a row inside a provider panel,
 * where the panel's border already bounds the rows, so the row keeps its own
 * padding and the last one drops its rule onto the panel's. `categoryHeader`
 * is the switch that governs a panel, worn as its header band.
 */
export type RowPlacement = "card" | "category" | "categoryHeader"

const ROW: Record<RowPlacement, string> = {
  // `last:border-b-0` for the same reason `category` has always had it: a rule
  // separates a row from what follows, and the last row in a run has nothing
  // following. In an editable section it got away without one while a `pt-6`
  // action row came after the last card-level row; after the Save/Cancel move
  // that row is gone for any section that is not Email and has no overrides —
  // which is every section in its default state — and the rule was left
  // floating in the card's own bottom padding. A bootstrap section never had
  // that action row at all, so there the rule had always been the dangling one.
  //
  // Where something DOES follow the rows — the companion-page footer on
  // SecretManagement and DataRetention — the seam is drawn by that footer's own
  // `border-t` in `section-form`, which also disappears with the footer when
  // the permission gate hides it. A rule on the row could never do that.
  // `data-[dirty=true]:ps-2.5` is the 2px the rail takes, given back — see
  // ROW_BASE. 12 − 2 = 10 = `ps-2.5`.
  card: `${ROW_BASE} border-b last:border-b-0 -mx-3 w-[calc(100%+1.5rem)] px-3 py-4 data-[dirty=true]:ps-2.5`,
  // 16 − 2 = 14 = `ps-3.5`.
  category: `${ROW_BASE} border-b last:border-b-0 px-4 py-4 data-[dirty=true]:ps-3.5`,
  // Full-strength `muted`, not a fraction of it: the palette's only tint step
  // is 3% in light mode, so anything less than all of it is not a band. The
  // hover tint is cancelled for the same reason — at 50% it would LIGHTEN this
  // row in dark mode, where `muted` sits above `card`.
  //
  // The auto margin is what keeps the mark against the name. This is the one
  // row with THREE children, and `justify-between` splits the slack into two
  // gaps — parking the text block in the middle of the row, adrift from the
  // logo that identifies it. The text block claims the slack itself instead,
  // so the mark and the name read as one unit at the start and the switch
  // still sits at the end. Logical (`me-`), so it holds in both directions.
  categoryHeader: `${ROW_BASE} rounded-t-xl border-b bg-muted px-4 py-3.5 hover:bg-muted focus-within:bg-muted data-[dirty=true]:ps-3.5 [&>[data-slot=field-content]]:me-auto`,
}

/**
 * Explanatory text stops at a comfortable measure rather than the card edge.
 *
 * 42rem at the 12px FieldDescription size is about 107 characters per line,
 * above the 75 that reads comfortably. It is NOT narrowed here, and the reason
 * is counter-intuitive: what the eye measures across a row is the ink, not the
 * box. Shrinking the text block does not shrink the gap between a hint and its
 * control — it widens the empty space between them, because the hint is the
 * thing that bridges the distance. Fixing the measure honestly means changing
 * the shared FieldDescription type size, which is a workspace-wide typographic
 * decision and out of scope here. 107 against 75: written down so the next
 * reader knows it was weighed rather than missed.
 */
const TEXT_BLOCK = "max-w-2xl"

/**
 * Control width, sized to the value it holds. Declared on the ROW targeting
 * its last child rather than on the control: the responsive Field variant
 * already sets `[&>*]:w-auto` once the row is horizontal, and a plain width
 * class on the control loses to it on specificity. Below the row breakpoint
 * the primitive's own `w-full` still applies, so stacked controls fill the
 * width as they should.
 */
const CONTROL = {
  int: "@md/field-group:[&>*:last-child]:w-40",
  text: "@md/field-group:[&>*:last-child]:w-80",
  area: "@md/field-group:[&>*:last-child]:w-[28rem]",
} as const

/** The row's anchor; highlighted for a moment on arrival — see `section-form`. */
function anchorId(field: SystemSettingsField): string {
  return settingAnchorId(field.path ?? "")
}

/**
 * The id of a row's label, so two things can point at it: the row itself, which
 * is a `role="group"` and would otherwise be an unnamed group, and the enum
 * control, which is a `div role="radiogroup"` — `<label for>` associates only
 * with labelable elements, so the label the row already renders reaches the
 * toggle group through `aria-labelledby` or not at all.
 */
function labelId(field: SystemSettingsField): string {
  return `${anchorId(field)}-label`
}

/** The id of the unsaved line, which names the row alongside its label. */
function unsavedId(field: SystemSettingsField): string {
  return `${anchorId(field)}-unsaved`
}

/**
 * What names the row's `role="group"`: its label, and — while the row is one of
 * the changed ones — the unsaved line as well.
 *
 * A group's name is announced on entering it, which is exactly when "this is
 * one of the rows you changed, and it was 0" is worth hearing. The rail says it
 * to the eye and nothing said it to a reader: the line is a `FieldDescription`,
 * and the form primitive describes its control with the hint and the validation
 * message only. Naming the group is the one channel available without taking an
 * id the hint already owns.
 */
function rowLabelledBy(field: SystemSettingsField, dirty: boolean): string {
  return dirty ? `${labelId(field)} ${unsavedId(field)}` : labelId(field)
}

function useFieldTexts(sectionI18n: string | undefined, field: SystemSettingsField) {
  const { t } = useTranslation()
  const key = fieldI18nKey(field.path ?? "")
  const base = sectionI18n ? `systemSettings.${sectionI18n}.${key}` : null
  // Fall back to the raw path so an untranslated section stays usable.
  const label = base ? t(base, { defaultValue: field.path ?? "" }) : (field.path ?? "")
  const hint = base ? t(`${base}Hint`, { defaultValue: "" }) : ""
  return { label, hint }
}

function FieldBadges({ field }: { field: SystemSettingsField }) {
  const { t } = useTranslation()
  return (
    <>
      {field.source === "database" ? (
        <Badge variant="secondary">{t("systemSettings.overridden")}</Badge>
      ) : null}
      {field.isPendingRestart ? (
        <Badge variant="destructive">{t("systemSettings.pendingRestart")}</Badge>
      ) : field.restartRequired ? (
        <Badge variant="outline">{t("systemSettings.restartRequired")}</Badge>
      ) : null}
    </>
  )
}

/** Shows the default a customized field would fall back to. */
function BaselineNote({ field }: { field: SystemSettingsField }) {
  const { t } = useTranslation()
  const labels = useValueLabels()
  if (field.source !== "database") return null
  const rendered = renderSettingValue(field.baselineValue, labels)
  return (
    <FieldDescription>
      {t("systemSettings.fileValue", { value: isolateFirstStrong(rendered) })}
    </FieldDescription>
  )
}

/**
 * The mark the operator is hunting for: this row is one of the ones they
 * changed, and here is what it used to say.
 *
 * FIRST in the description stack, above the hint. It is the answer to "which
 * row did I touch"; under a paragraph of hint prose it would be found by
 * reading rather than by scanning, which is the whole of the problem.
 *
 * `text-foreground font-medium` is a LUMINANCE step, not a colour: every
 * non-destructive token in this palette is `oklch(L 0 0)`, and muted-foreground
 * to foreground is 4.73:1 → 19.79:1 on card in light and 6.91:1 → 17.16:1 in
 * dark. Against the muted line beside it that is 4.18:1 in light and 2.48:1 in
 * dark — the step is real in both themes and clearly weaker in the dark one,
 * which is why the weight moves with the ink rather than the ink alone doing
 * the work. `command` marks a selected row's own description the same way at
 * the same size. The three coloured alternatives are all taken: destructive
 * belongs to `FormMessage` two lines down, primary belongs to links inside a
 * description, and a Badge would put a control-shaped object in a stack of
 * sentences.
 *
 * It carries an id because the row NAMES itself with this line while it is
 * showing — see `rowLabelledBy`. A field description is not in the control's
 * `aria-describedby` (the primitive reserves that for the hint and the
 * message), so without that the one sentence this whole feature exists to write
 * would be readable by everyone except the operator who cannot see the rail.
 */
function UnsavedNote({ id, previousValue }: { id: string; previousValue: unknown }) {
  const { t } = useTranslation()
  const labels = useValueLabels()
  const rendered = renderSettingValue(previousValue, labels)
  // Joined with the middot `FieldConstraints` already sets its parts with, so
  // the two lines of the same stack punctuate alike.
  const line = [
    t("systemSettings.unsaved"),
    t("systemSettings.previousValue", { value: isolateFirstStrong(rendered) }),
  ].join(" · ")
  return (
    <FieldDescription id={id} className="font-medium text-foreground">
      {line}
    </FieldDescription>
  )
}

/** A secret-owned field: the value lives in Secret Management, never here. */
export function SecretFieldRow({
  sectionI18n,
  field,
  placement = "card",
}: {
  sectionI18n: string | undefined
  field: SystemSettingsField
  placement?: RowPlacement
}) {
  const { t } = useTranslation()
  const { label } = useFieldTexts(sectionI18n, field)
  return (
    <Field
      orientation="responsive"
      className={ROW[placement]}
      id={anchorId(field)}
      aria-labelledby={labelId(field)}
    >
      <FieldContent className={TEXT_BLOCK}>
        <FieldLabel id={labelId(field)}>{label}</FieldLabel>
        <FieldDescription>{t("systemSettings.managedInSecrets")}</FieldDescription>
      </FieldContent>
      <Button variant="outline" size="sm" asChild>
        <Link to={SECRET_KEYS_ROUTE}>{t("systemSettings.openSecrets")}</Link>
      </Button>
    </Field>
  )
}

/** A read-only field: shown for transparency, not editable. */
export function ReadOnlyFieldRow({
  sectionI18n,
  field,
  placement = "card",
  sectionKey,
  dirty = false,
  previousValue,
}: {
  sectionI18n: string | undefined
  field: SystemSettingsField
  placement?: RowPlacement
  /**
   * The backend section key. Direction is a property of a setting's VALUE, not
   * of its kind, and the registry that knows which is which is keyed by section.
   * Optional so a caller that has not been updated keeps today's behaviour.
   */
  sectionKey?: string
  /**
   * This field differs from the value the form opened with. Optional, like
   * `disabled`: a caller that does not track it renders today's row.
   */
  dirty?: boolean
  /** The value the field opened with. Read only while `dirty` is true. */
  previousValue?: unknown
}) {
  const { t } = useTranslation()
  const { label, hint } = useFieldTexts(sectionI18n, field)
  const value = field.effectiveValue
  return (
    <Field
      orientation="responsive"
      className={`${ROW[placement]} ${CONTROL.text}`}
      id={anchorId(field)}
      aria-labelledby={rowLabelledBy(field, dirty)}
      data-disabled
      // `true`, never bare: a valueless `data-dirty` serialises to the empty
      // string, which `data-[dirty=true]` does not match, and the rail would
      // never appear.
      data-dirty={dirty ? true : undefined}
    >
      <FieldContent className={TEXT_BLOCK}>
        <FieldLabel id={labelId(field)}>
          {label}
          <Badge variant="outline">{t("systemSettings.readOnly")}</Badge>
        </FieldLabel>
        {dirty ? (
          <UnsavedNote id={unsavedId(field)} previousValue={previousValue} />
        ) : null}
        {hint ? <FieldDescription>{hint}</FieldDescription> : null}
      </FieldContent>
      {/* Naming the row's `role="group"` does not name the control inside it —
          a group's name does not reach its children. This row's label carries
          no `htmlFor` (there is no form control id to point at), so without
          this the value reads as an unnamed read-only text box and nothing
          says which setting it belongs to. */}
      <Input
        value={value === null || value === undefined ? "" : String(value)}
        disabled
        aria-labelledby={labelId(field)}
        dir={valueDirection(sectionKey, field.path ?? "")}
      />
    </Field>
  )
}

/**
 * One editable setting bound into the section form. Form values are kept in
 * input-friendly shapes (numbers as strings, arrays as one-per-line text)
 * and normalized on submit.
 */
export function SettingField({
  control,
  sectionI18n,
  field,
  placement = "card",
  media,
  sectionKey,
  disabled = false,
  dirty = false,
  previousValue,
}: {
  control: Control<FieldValues>
  sectionI18n: string | undefined
  field: SystemSettingsField
  placement?: RowPlacement
  /**
   * A mark shown before the label — the provider's logo on the row that heads
   * its panel. Rendered as given: the row applies no sizing, because the
   * caller is the one that knows what the mark is.
   */
  media?: React.ReactNode
  /**
   * The backend section key. Direction is a property of a setting's VALUE, not
   * of its kind — `SmtpHost` and `SenderName` are both strings and only one is
   * prose — and the registry that knows which is which is keyed by section,
   * because seven field paths repeat across sections. Optional so a caller that
   * has not been updated keeps today's behaviour rather than guessing.
   */
  sectionKey?: string
  /**
   * The switch governing this field's category is off. The control is inert and
   * the row says so; the VALUE is untouched, so switching the category back on
   * finds the credentials still there.
   */
  disabled?: boolean
  /**
   * This field differs from the value the form opened with — which is NOT the
   * same as differing from the configuration file. A setting somebody
   * customized last month differs from the file and nobody touched it this
   * session; marking it would point the operator at a row they never edited.
   * The caller sources this from the form's own dirty fields for that reason.
   *
   * Optional, like `disabled`: a caller that does not track it renders today's
   * row.
   */
  dirty?: boolean
  /**
   * The value this field opened with — the last one the form and the server
   * agreed on, not the configuration-file baseline that `BaselineNote` already
   * renders on its own line. Read only while `dirty` is true.
   */
  previousValue?: unknown
}) {
  const { t } = useTranslation()
  const { label, hint } = useFieldTexts(sectionI18n, field)
  const name = formFieldName(field.path ?? "")
  const kind = field.kind ?? "string"
  const heading = placement === "categoryHeader"
  const dir = valueDirection(sectionKey, field.path ?? "")

  // The hint, the bounds, the default-value note and any validation message
  // all belong to the text block; as direct row children they would become
  // extra columns.
  //
  // Bounds come from the registry, so this one mount states them for every
  // section the console has — and the hint copy never repeats a number that
  // could then drift from the value actually enforced.
  const textBlock = (extraHint?: string) => (
    <FieldContent className={TEXT_BLOCK}>
      {/* A switch's label reads as prose beside its control, so it drops the
          emphasis the other labels carry — except when the switch heads a
          category, where it names everything under it and would otherwise be
          the lightest text in a panel it governs. */}
      <FormLabel
        id={labelId(field)}
        className={kind === "bool" && !heading ? "font-normal" : undefined}
      >
        {label}
        <FieldBadges field={field} />
      </FormLabel>
      {/* Above the hint, not below it — see `UnsavedNote`. */}
      {dirty ? (
        <UnsavedNote id={unsavedId(field)} previousValue={previousValue} />
      ) : null}
      {hint || extraHint ? (
        <FormDescription>{[hint, extraHint].filter(Boolean).join(" ")}</FormDescription>
      ) : null}
      <FieldConstraints
        min={field.min}
        max={field.max}
        defaultValue={field.defaultValue}
      />
      <BaselineNote field={field} />
      <FormMessage />
    </FieldContent>
  )

  if (kind === "bool") {
    return (
      <FormField
        control={control}
        name={name}
        render={({ field: rhf }) => (
          <FormItem
            orientation="horizontal"
            className={ROW[placement]}
            id={anchorId(field)}
            aria-labelledby={rowLabelledBy(field, dirty)}
            data-disabled={disabled ? true : undefined}
            data-dirty={dirty ? true : undefined}
          >
            {media}
            {textBlock()}
            <FormControl>
              <Switch
                checked={rhf.value === true}
                onCheckedChange={rhf.onChange}
                disabled={disabled}
              />
            </FormControl>
          </FormItem>
        )}
      />
    )
  }

  if (kind === "enum") {
    return (
      <FormField
        control={control}
        name={name}
        render={({ field: rhf }) => (
          <FormItem
            orientation="responsive"
            className={ROW[placement]}
            id={anchorId(field)}
            aria-labelledby={rowLabelledBy(field, dirty)}
            data-disabled={disabled ? true : undefined}
            data-dirty={dirty ? true : undefined}
          >
            {textBlock()}
            <FormControl>
              <ToggleGroup
                type="single"
                spacing={2}
                variant="outline"
                // Wraps only when the row is too narrow to hold every option.
                className="flex-wrap"
                aria-labelledby={labelId(field)}
                disabled={disabled}
                value={typeof rhf.value === "string" ? rhf.value : ""}
                onValueChange={(value) => {
                  if (value) rhf.onChange(value)
                }}
              >
                {(field.allowedValues ?? []).map((option) => (
                  <ToggleGroupItem key={option} value={option}>
                    {option}
                  </ToggleGroupItem>
                ))}
              </ToggleGroup>
            </FormControl>
          </FormItem>
        )}
      />
    )
  }

  if (kind === "stringArray") {
    return (
      <FormField
        control={control}
        name={name}
        render={({ field: rhf }) => (
          <FormItem
            orientation="responsive"
            className={`${ROW[placement]} ${CONTROL.area}`}
            id={anchorId(field)}
            aria-labelledby={rowLabelledBy(field, dirty)}
            data-disabled={disabled ? true : undefined}
            data-dirty={dirty ? true : undefined}
          >
            {textBlock(t("systemSettings.arrayFieldHint"))}
            <FormControl>
              <Textarea
                value={typeof rhf.value === "string" ? rhf.value : ""}
                onChange={rhf.onChange}
                onBlur={rhf.onBlur}
                rows={4}
                dir={dir}
                disabled={disabled}
              />
            </FormControl>
          </FormItem>
        )}
      />
    )
  }

  const isInt = kind === "int"
  return (
    <FormField
      control={control}
      name={name}
      rules={
        isInt
          ? {
              validate: (raw: unknown) => {
                const text = String(raw ?? "").trim()
                if (text.length === 0) return t("validation.required")
                if (!/^-?\d+$/.test(text)) return t("validation.wholeNumber")
                const value = Number(text)
                if (field.min !== null && field.min !== undefined && value < Number(field.min))
                  return t("validation.min", { min: field.min })
                if (field.max !== null && field.max !== undefined && value > Number(field.max))
                  return t("validation.max", { max: field.max })
                return true
              },
            }
          : undefined
      }
      render={({ field: rhf }) => (
        <FormItem
          orientation="responsive"
          className={`${ROW[placement]} ${isInt ? CONTROL.int : CONTROL.text}`}
          id={anchorId(field)}
          aria-labelledby={rowLabelledBy(field, dirty)}
          data-disabled={disabled ? true : undefined}
          data-dirty={dirty ? true : undefined}
        >
          {textBlock()}
          <FormControl>
            <Input
              value={typeof rhf.value === "string" ? rhf.value : ""}
              onChange={rhf.onChange}
              onBlur={rhf.onBlur}
              inputMode={isInt ? "numeric" : undefined}
              dir={dir}
              disabled={disabled}
            />
          </FormControl>
        </FormItem>
      )}
    />
  )
}
