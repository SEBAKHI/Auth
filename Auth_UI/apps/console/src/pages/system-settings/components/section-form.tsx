import * as React from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { ChevronDown, TriangleAlert } from "lucide-react"
import {
  useForm,
  useWatch,
  type Control,
  type FieldErrors,
  type FieldValues,
} from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useSearchParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { RequirePermission } from "@authsystem/auth/require-permission"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import {
  FieldDescription,
  FieldGroup,
  FieldLegend,
  FieldSet,
} from "@authsystem/ui/field"
import { Form } from "@authsystem/ui/form"
import {
  Item,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemTitle,
} from "@authsystem/ui/item"
import {
  Popover,
  PopoverContent,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from "@authsystem/ui/popover"
import { Spinner } from "@authsystem/ui/spinner"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"

import {
  requiredCredentials,
  resolveSectionLayout,
  type CategoryBlock,
  type ResolvedBlock,
} from "../lib/section-layout"
import {
  SECTION_COMPANION_PAGES,
  SECTION_I18N,
  SETTINGS_QUERY_KEY,
  fieldI18nKey,
  formFieldName,
  isHighImpactPath,
  settingAnchorId,
  type SystemSettingsDto,
  type SystemSettingsField,
  type SystemSettingsSection,
} from "../lib/sections"
import {
  isolateFirstStrong,
  renderSettingValue,
  useValueLabels,
} from "../lib/setting-value"
import {
  ReadOnlyFieldRow,
  SecretFieldRow,
  SettingField,
  type RowPlacement,
} from "./setting-field"

/** Input-friendly form value for a field (numbers/arrays as text). */
function toFormValue(field: SystemSettingsField): string | boolean {
  const effective = field.effectiveValue
  switch (field.kind) {
    case "bool":
      return effective === true
    case "stringArray":
      return Array.isArray(effective) ? effective.join("\n") : ""
    default:
      return effective === null || effective === undefined ? "" : String(effective)
  }
}

/** Normalized value to compare against the baseline and send to the API. */
function toApiValue(field: SystemSettingsField, raw: unknown): unknown {
  switch (field.kind) {
    case "bool":
      return raw === true
    case "int":
      return Number(String(raw ?? "").trim())
    case "stringArray":
      return String(raw ?? "")
        .split("\n")
        .map((line) => line.trim())
        .filter((line) => line.length > 0)
    default:
      return String(raw ?? "")
  }
}

function normalizedBaseline(field: SystemSettingsField): unknown {
  const baseline = field.baselineValue
  switch (field.kind) {
    case "bool":
      return baseline === true
    case "int":
      return baseline === null || baseline === undefined ? null : Number(baseline)
    case "stringArray":
      return Array.isArray(baseline) ? baseline : []
    default:
      return baseline === null || baseline === undefined ? "" : String(baseline)
  }
}

function sameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(a ?? null) === JSON.stringify(b ?? null)
}

/** One unsaved change, as the bar's list states it. */
interface UnsavedChange {
  /** The registry path, so activating the entry can find the row. */
  path: string
  label: string
  previous: string
  current: string
}

/**
 * Whether a row's field differs from the values the form opened with, and what
 * it opened with. One lookup rather than two props threaded in parallel, so a
 * row can never be told it is dirty without being told what it was.
 */
type DirtyLookup = (path: string) => { dirty: boolean; previousValue: unknown }

/** No form, no unsaved state: the bootstrap sections render rows through here too. */
const NO_DIRTY: DirtyLookup = () => ({ dirty: false, previousValue: undefined })

/** Builds the sparse nested override object ("A:B:C" → {A:{B:{C: value}}}). */
function setNested(target: Record<string, unknown>, path: string, value: unknown) {
  const segments = path.split(":")
  let cursor = target
  for (let i = 0; i < segments.length - 1; i++) {
    cursor[segments[i]] ??= {}
    cursor = cursor[segments[i]] as Record<string, unknown>
  }
  cursor[segments[segments.length - 1]] = value
}

/**
 * Puts a row on screen, marks it, and puts the caret in it.
 *
 * Scrolling alone is what a search result used to get: the row arrived, the
 * ring faded after two seconds, and the next keystroke went wherever focus had
 * been left — usually the command palette that had just closed. Arriving at a
 * setting means being able to change it.
 *
 * The ring is not a nicety here. A row inside a switched-off category holds no
 * control that can take focus, and that is a state an invalid submit really can
 * land on: type a letter into a whole-number field, switch its category off,
 * press Save. The ring is then the only thing that says WHICH row, so it is
 * unconditional and the focus is best-effort.
 *
 * The 2s timer is deliberately not cleaned up. `removeAttribute` on a detached
 * node is a no-op, and each call only ever touches the element it was given, so
 * a stray timer from a row that has since unmounted costs nothing.
 */
function focusRow(element: HTMLElement | null) {
  if (!element) return
  const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
  element.scrollIntoView({
    block: "center",
    behavior: reduceMotion ? "auto" : "smooth",
  })
  element.setAttribute("data-highlight", "true")
  setTimeout(() => element.removeAttribute("data-highlight"), 2000)

  const control = element.querySelector<HTMLElement>(
    "input:not([disabled]), textarea:not([disabled]), button:not([disabled]), a[href]"
  )
  if (control) {
    // `preventScroll`: focusing must not cancel the smooth scroll just started.
    control.focus({ preventScroll: true })
    return
  }
  // Nothing inside can take focus. Land on the row's own group instead, so a
  // screen reader reads the label and the message rather than staying wherever
  // it was.
  element.tabIndex = -1
  element.focus({ preventScroll: true })
}

/** A list of names in the reader's language ("a, b and c"), never a bare join. */
function listOf(language: string, items: string[]): string {
  try {
    return new Intl.ListFormat(language, {
      style: "long",
      type: "conjunction",
    }).format(items)
  } catch {
    return items.join(", ")
  }
}

/**
 * Brings the setting named by `?field=` into view and marks it briefly, so a
 * search result lands on one row rather than on a page of forty.
 *
 * A missing target is not an error: a deployed console can be a release behind
 * the backend and simply not render a field the server already knows about.
 * The navigation still put the user on the right section.
 */
function useFieldAnchor(sectionKey: string) {
  const [searchParams] = useSearchParams()
  const target = searchParams.get("field")

  React.useEffect(() => {
    if (!target) return
    const element = document.getElementById(settingAnchorId(target))
    if (!element) return
    // Scroll, ring and focus now live in one helper, shared with the invalid
    // submit — two arrivals at a row that must not behave differently.
    focusRow(element)
    // Re-runs when the section changes too, since the row only exists once
    // its own section is rendered.
  }, [target, sectionKey])
}

/**
 * The way out of a section whose real controls are operations rather than
 * settings. Generic on purpose: which sections have one is declared in
 * `SECTION_COMPANION_PAGES`, and a section without an entry renders nothing
 * here. The permission gate is the companion page's own, not the section's.
 */
function SectionCompanionAction({ sectionKey }: { sectionKey: string }) {
  const { t } = useTranslation()
  const companion = SECTION_COMPANION_PAGES[sectionKey]
  if (!companion) return null

  return (
    <RequirePermission permission={companion.permission}>
      {/* `border-t` is the seam between the settings and this action, and it is
          the footer's job rather than the last row's: a card-level row drops
          its own rule when it is last in its run, and on a section whose card
          IS this link (SecretManagement) that left the button hanging off the
          rows with nothing between them. Drawing it here also means the seam
          disappears with the footer when the permission gate hides it, which a
          rule on the row could never do. The primitive already reserves
          `[.border-t]:pt-(--card-spacing)` for exactly this. */}
      <CardFooter className="border-t">
        <Button asChild>
          <Link to={companion.route}>{t(companion.actionLabelKey)}</Link>
        </Button>
      </CardFooter>
    </RequirePermission>
  )
}

/**
 * One field as whichever kind of row it is, so the two places that render a
 * list of settings — the section card and a category panel — decide only
 * WHERE a row goes, never what it is.
 *
 * `control` is optional because the bootstrap sections have no form: those are
 * read before the database layer exists, so every field there is shown rather
 * than edited.
 */
function SettingRow({
  field,
  sectionKey,
  sectionI18n,
  control,
  placement = "card",
  media,
  disabled = false,
  dirty = false,
  previousValue,
}: {
  field: SystemSettingsField
  sectionKey: string
  sectionI18n: string | undefined
  control?: Control<FieldValues>
  placement?: RowPlacement
  media?: React.ReactNode
  disabled?: boolean
  /** The field differs from the value the form opened with. */
  dirty?: boolean
  /** What it opened with. Only read while `dirty`. */
  previousValue?: unknown
}) {
  if (field.sensitive) {
    // No `disabled`: the value lives in Secret management, and a category
    // switched off must not strand the one field that can only be fixed there.
    return (
      <SecretFieldRow
        sectionI18n={sectionI18n}
        field={field}
        placement={placement}
      />
    )
  }
  if (field.readOnly || !control) {
    return (
      <ReadOnlyFieldRow
        sectionI18n={sectionI18n}
        sectionKey={sectionKey}
        field={field}
        placement={placement}
      />
    )
  }
  // `dirty` and `previousValue` go only where `disabled` goes, and for the same
  // reason: a secret-owned or read-only row is not in the form at all, so it
  // holds no value that can differ from the one the form opened with.
  return (
    <SettingField
      control={control}
      sectionI18n={sectionI18n}
      sectionKey={sectionKey}
      field={field}
      placement={placement}
      media={media}
      disabled={disabled}
      dirty={dirty}
      previousValue={previousValue}
    />
  )
}

/**
 * The one state a gated category can be in that reports itself nowhere else:
 * switched on with a setting it needs still blank. Nothing errors, nothing
 * logs, and the feature simply never happens — so the panel says it, from the
 * values in the form rather than from what was last saved.
 *
 * Only the settings this form can actually read are counted. A secret-owned
 * field's value lives in Secret management, so its emptiness is not knowable
 * here and is not claimed.
 */
function CategoryIncomplete({
  control,
  sectionI18n,
  switchField,
  credentials,
}: {
  control: Control<FieldValues>
  sectionI18n: string | undefined
  switchField: SystemSettingsField
  credentials: SystemSettingsField[]
}) {
  const { t } = useTranslation()
  const names = React.useMemo(
    () => [switchField, ...credentials].map((f) => formFieldName(f.path ?? "")),
    [switchField, credentials]
  )
  const [enabled, ...values] = useWatch({ control, name: names }) as unknown[]

  const title = sectionI18n
    ? t(`systemSettings.${sectionI18n}.incompleteTitle`, { defaultValue: "" })
    : ""

  if (enabled !== true || !title) return null
  if (values.every((value) => String(value ?? "").trim().length > 0)) return null

  return (
    <div className="px-4 pt-4">
      <Alert variant="destructive">
        <TriangleAlert />
        <AlertTitle>{title}</AlertTitle>
        <AlertDescription>
          {t(`systemSettings.${sectionI18n}.incompleteBody`, { defaultValue: "" })}
        </AlertDescription>
      </Alert>
    </div>
  )
}

/**
 * A category, drawn as a panel: the switch that governs it wears the panel's
 * header, and the settings it gates sit under it inside the same border. Where
 * the subject has an official mark it goes in the header too — a panel is
 * found by its logo before its title is read.
 *
 * The guard lives out here so the body can watch the switch: hooks cannot sit
 * behind an early return, and the body's whole job is to follow that value.
 */
function CategoryPanel({
  sectionKey,
  sectionI18n,
  control,
  entry,
  dirtyOf = NO_DIRTY,
}: {
  sectionKey: string
  sectionI18n: string | undefined
  control: Control<FieldValues>
  entry: ResolvedBlock
  dirtyOf?: DirtyLookup
}) {
  const { block, switchField } = entry
  if (block.kind !== "category" || !switchField) return null
  return (
    <CategoryPanelBody
      sectionKey={sectionKey}
      sectionI18n={sectionI18n}
      control={control}
      block={block}
      switchField={switchField}
      fields={entry.fields}
      dirtyOf={dirtyOf}
    />
  )
}

/**
 * `min-w-0`: a fieldset's own `min-inline-size: min-content` would stop it
 * shrinking with the card, and the overflow would be the CARD's, not this
 * element's — the kind of break that shows up two components away.
 *
 * The panel says off by BEING off. A category switched off changes nothing
 * until it is switched on, so its settings are inert, and rows that still
 * accept typing while nothing they say can happen are a promise the page does
 * not keep. The values themselves are untouched: `disabled` here is a DOM prop
 * on the control, not RHF's register option, so `_formValues` still holds every
 * credential and turning the category back on finds them exactly as they were.
 */
function CategoryPanelBody({
  sectionKey,
  sectionI18n,
  control,
  block,
  switchField,
  fields,
  dirtyOf = NO_DIRTY,
}: {
  sectionKey: string
  sectionI18n: string | undefined
  control: Control<FieldValues>
  block: CategoryBlock
  switchField: SystemSettingsField
  fields: SystemSettingsField[]
  dirtyOf?: DirtyLookup
}) {
  const Icon = block.icon
  const gate = useWatch({
    control,
    name: formFieldName(switchField.path ?? ""),
  })
  const gated = gate !== true

  return (
    <FieldSet
      // A fieldset with no `legend` is an unnamed group, and this one has a name
      // on screen: the switch row wears it as the panel's header. It has to be
      // in the accessibility tree too, because the message that says how to
      // reach an invalid value inside a switched-off category names this
      // category — and a reader sent looking for it would otherwise find a
      // group called nothing. The id is the one `setting-field` publishes for
      // every row's label.
      aria-labelledby={`${settingAnchorId(switchField.path ?? "")}-label`}
      className="min-w-0 gap-0 rounded-xl border"
    >
      {/* The lookup answers with both halves at once — dirty, and what it was —
          so the pair is spread rather than passed as two props that could drift
          apart at one of the four call sites. */}
      <SettingRow
        field={switchField}
        sectionKey={sectionKey}
        sectionI18n={sectionI18n}
        control={control}
        placement="categoryHeader"
        media={Icon ? <Icon className="size-5 shrink-0" /> : undefined}
        {...dirtyOf(switchField.path ?? "")}
      />
      <CategoryIncomplete
        control={control}
        sectionI18n={sectionI18n}
        switchField={switchField}
        credentials={requiredCredentials(fields)}
      />
      {fields.map((field) => (
        <SettingRow
          key={field.path}
          field={field}
          sectionKey={sectionKey}
          sectionI18n={sectionI18n}
          control={control}
          placement="category"
          disabled={gated}
          {...dirtyOf(field.path ?? "")}
        />
      ))}
    </FieldSet>
  )
}

/**
 * A group, drawn as a legend over rows that stay at card level. No border:
 * proximity and a heading already group them, and a container around every
 * related pair would spend the one device that means "this has an off switch".
 *
 * The air goes on the MARGIN, not the padding — a `legend` is laid out against
 * the fieldset's border box, so padding-top moves the rows down and leaves the
 * heading welded to whatever sits above it. An untranslated heading renders as
 * no heading rather than as a raw key.
 */
function GroupBlockRows({
  sectionKey,
  sectionI18n,
  control,
  headingKey,
  fields,
  dirtyOf = NO_DIRTY,
}: {
  sectionKey: string
  sectionI18n: string | undefined
  control?: Control<FieldValues>
  headingKey: string
  fields: SystemSettingsField[]
  dirtyOf?: DirtyLookup
}) {
  const { t } = useTranslation()
  const base = sectionI18n ? `systemSettings.${sectionI18n}.${headingKey}` : null
  const heading = base ? t(base, { defaultValue: "" }) : ""
  const description = base ? t(`${base}Description`, { defaultValue: "" }) : ""

  // The fieldset renders whether or not the heading translated: it is the one
  // flex item holding these rows flush against each other, and dropping it
  // would let the parent's gap fall BETWEEN ruled rows that are meant to read
  // as one run.
  return (
    <FieldSet className="mt-4 min-w-0 gap-0 first:mt-0">
      {heading ? <FieldLegend>{heading}</FieldLegend> : null}
      {description ? (
        <FieldDescription className="-mt-2 mb-3">{description}</FieldDescription>
      ) : null}
      {fields.map((field) => (
        <SettingRow
          key={field.path}
          field={field}
          sectionKey={sectionKey}
          sectionI18n={sectionI18n}
          control={control}
          {...dirtyOf(field.path ?? "")}
        />
      ))}
    </FieldSet>
  )
}

/**
 * Every unsaved change in the section, one entry each, as a list that goes
 * there.
 *
 * The count alone told an operator how many rows they had touched and nothing
 * about WHICH — on a section of forty rows that is a scroll, not an answer.
 * Each entry names the setting and both of its values, and activating one lands
 * on the row through the same helper a `?field=` deep link uses.
 *
 * `max-h` with its own overflow rather than `ScrollArea`: a section can carry
 * nineteen fields, and the Radix scroll area needs a ResizeObserver that this
 * repo's jsdom tests do not have. `p-1` is not decoration — the focus ring is
 * drawn OUTSIDE the entry, and a scroll container clips it flush.
 */
function UnsavedChangesList({
  changes,
  onJump,
}: {
  changes: UnsavedChange[]
  onJump: (path: string) => void
}) {
  const { t } = useTranslation()
  return (
    <ItemGroup className="max-h-72 overflow-y-auto p-1">
      {changes.map((change) => (
        // `ItemGroup` declares `role="list"`, and a list whose children are
        // buttons owns no list items at all — a reader counts the entries from
        // what the list OWNS and announces "0 items" over three changes. The
        // count is the first thing this panel says, so the wrapper is not
        // pedantry here.
        <div role="listitem" key={change.path}>
          <Item
            asChild
            size="sm"
            // `outline` rather than `muted`, and the hover tint is halved. The
            // variants tint an anchor on hover and say nothing about a button,
            // so an entry that moves the page needs its own answer to the
            // pointer — but `muted` at full strength puts `ItemDescription`
            // (muted-foreground, 14px) at 4.34:1 on this ground, under the 4.5
            // that normal-size text has to clear. A border instead of a fill
            // draws the hit target and leaves the ground pale enough to read
            // on: 4.73:1 at rest, 4.53:1 hovered.
            variant="outline"
            className="hover:bg-muted/50"
          >
            <button
              type="button"
              className="text-start"
              onClick={() => onJump(change.path)}
            >
              <ItemContent>
                {/* Unclamped, both lines. The primitive clamps a title to one
                    line and a description to two, which is right for a card
                    and wrong here: the half that gets cut is the previous
                    value, which is the one fact the operator opened this panel
                    to read. */}
                <ItemTitle className="line-clamp-none">{change.label}</ItemTitle>
                {/* Both values, in the reader's own words, with no arrow
                    between them: an arrow is a direction, and in Arabic it
                    would point from the new value back to the old one. The
                    middot is the separator `FieldConstraints` already uses for
                    facts about one field. */}
                <ItemDescription className="line-clamp-none">
                  {[
                    t("systemSettings.previousValue", {
                      value: isolateFirstStrong(change.previous),
                    }),
                    t("systemSettings.currentValue", {
                      value: isolateFirstStrong(change.current),
                    }),
                  ].join(" · ")}
                </ItemDescription>
              </ItemContent>
            </button>
          </Item>
        </div>
      ))}
    </ItemGroup>
  )
}

export function SectionForm({ section }: { section: SystemSettingsSection }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const sectionKey = section.key ?? ""
  const sectionI18n = SECTION_I18N[sectionKey]
  // The sticky bar sits OUTSIDE the card, so its Save reaches this form through
  // the `form` attribute rather than through the DOM tree.
  const formId = `system-settings-form-${sectionKey}`
  // The same three words the rows use, from the same hook — the bar's list and
  // the row it points at have to name a switch and an absent value alike.
  const labels = useValueLabels()

  const [confirmReset, setConfirmReset] = React.useState(false)
  /**
   * The change set waiting on a confirmation. Non-null means the dialog is
   * open and these are the values it will send — held rather than re-read, so
   * what the operator confirmed is exactly what is sent.
   */
  const [pendingValues, setPendingValues] = React.useState<FieldValues | null>(null)
  const [highImpactNames, setHighImpactNames] = React.useState<string[]>([])
  const [conflictFields, setConflictFields] = React.useState<string[] | null>(null)
  /** Whether the bar's list of unsaved changes is showing. */
  const [changesOpen, setChangesOpen] = React.useState(false)
  /** That list, taken when it opens — see `openChanges`. */
  const [changes, setChanges] = React.useState<UnsavedChange[]>([])
  /**
   * The row an entry in that list sent us to, handed over only once the popover
   * has actually closed: Radix returns focus to the trigger on close, and a row
   * focused before that would lose it again a frame later.
   */
  const jumpTargetRef = React.useRef<string | null>(null)
  /** Where focus goes when the bar that had it unmounts on a successful save. */
  const titleRef = React.useRef<HTMLDivElement>(null)
  /**
   * Where focus goes when a save FAILS. Pressing Save disables it while the
   * request is in flight, and a browser blurs the control it disables, so by
   * the time an answer arrives focus is on `<body>` and the next Tab restarts
   * at the top of the document. A conflict hands focus to its report, which is
   * the durable half of the message; every other failure hands it back to the
   * button that has to be pressed again.
   */
  const conflictRef = React.useRef<HTMLDivElement>(null)
  const saveRef = React.useRef<HTMLButtonElement>(null)

  useFieldAnchor(sectionKey)

  const fields = React.useMemo(() => section.fields ?? [], [section.fields])
  const editable = React.useMemo(
    () => fields.filter((f) => !f.sensitive && !f.readOnly),
    [fields]
  )
  const { blocks, general } = React.useMemo(
    () => resolveSectionLayout(sectionKey, fields),
    [sectionKey, fields]
  )

  const defaultValues = React.useMemo(() => {
    const values: FieldValues = {}
    for (const field of editable) {
      values[formFieldName(field.path ?? "")] = toFormValue(field)
    }
    return values
  }, [editable])

  /** The paths in the order the page draws them — which is not backend order. */
  const renderedPaths = React.useMemo(
    () =>
      [
        ...blocks.flatMap((entry) => [
          ...(entry.switchField ? [entry.switchField] : []),
          ...entry.fields,
        ]),
        ...general,
      ].map((f) => f.path ?? ""),
    [blocks, general]
  )

  /**
   * Which switch governs which setting. A row inside a switched-off category
   * holds an inert control, so an invalid value in one is an error the operator
   * cannot reach — and the only way out is to switch that category back on,
   * which means the message has to name it. See `onInvalid`.
   */
  const gatingSwitches = React.useMemo(() => {
    const governors: Record<string, SystemSettingsField> = {}
    for (const entry of blocks) {
      if (entry.block.kind !== "category" || !entry.switchField) continue
      for (const field of entry.fields) {
        governors[field.path ?? ""] = entry.switchField
      }
    }
    return governors
  }, [blocks])

  const form = useForm<FieldValues>({ defaultValues })
  const isDirty = form.formState.isDirty
  /**
   * What the operator changed THIS session — the only honest source for both
   * the mark on a row and the list in the bar.
   *
   * Not the save payload, which is every value that differs from the file
   * baseline: that set includes settings somebody customized months ago and
   * nobody has touched since, and marking those would point the operator at
   * rows they never edited.
   */
  const dirtyFields = form.formState.dirtyFields as Record<string, unknown>
  const dirtyCount = Object.keys(dirtyFields).length
  /**
   * The values the form opened with, which is what `dirtyFields` is measured
   * against — so a row's mark and the value it names can never disagree.
   *
   * The same set as `syncedRef` (every gesture that moves one calls
   * `form.reset` with the other), read from the form because a ref may not be
   * read during render, and every row asks this question during render.
   *
   * THE INVARIANT, since two names for one fact is how they drift apart: every
   * write to `syncedRef` is paired with a `form.reset` of the same object, and
   * no `reset` here passes `keepDefaultValues`. Break either half and the row
   * will say "was X" while the bar's list says "was Y" for the same setting,
   * with nothing failing. The list reads `syncedRef` directly (it can — it runs
   * in an event, not in render), which is what makes the pairing observable.
   */
  const openedWith = (form.formState.defaultValues ?? {}) as FieldValues
  const dirtyOf: DirtyLookup = (path) => {
    const name = formFieldName(path)
    const dirty = Boolean(dirtyFields[name])
    return { dirty, previousValue: dirty ? openedWith[name] : undefined }
  }

  /**
   * The values this form is currently in agreement with the server about.
   * Everything the conflict report says is measured against it: a field whose
   * server value has moved away from this is one somebody else changed.
   */
  const syncedRef = React.useRef<FieldValues>(defaultValues)

  /**
   * A cheap identity for the payload. Comparing the values rather than the
   * object catches the case a row version cannot: a refetch that returns the
   * same settings under a new object.
   */
  const signature = React.useMemo(() => JSON.stringify(defaultValues), [defaultValues])
  const appliedRef = React.useRef(signature)

  /**
   * A conflict report is a statement about UNSAVED work, so it lives exactly as
   * long as the work does. Save, Cancel and Reset each drop it themselves; this
   * covers the fourth way a form goes clean, which no callback here owns — the
   * operator typing their own value back by hand. Without it the alert would
   * survive the edit it describes and reappear on the next keystroke, welded to
   * a card that no longer disagrees with anything.
   *
   * Through RHF's subscription rather than the effect below, because that is
   * where state may be set in response to a store outside React.
   */
  React.useEffect(() => {
    const unsubscribe = form.subscribe({
      formState: { isDirty: true },
      callback: ({ isDirty: dirty }) => {
        if (!dirty) {
          setConflictFields(null)
          // The list of unsaved changes is a statement about the same work, and
          // the bar that holds it unmounts with it. Left standing, the open flag
          // would still be true the next time an edit brings the bar back, and
          // the popover would appear on its own.
          setChangesOpen(false)
        }
      },
    })
    return unsubscribe
  }, [form])

  /**
   * Adopting the server's values is a decision, not a consequence of fetching.
   *
   * A clean form has nothing to lose, so it takes the new values silently —
   * that is how a save's own refetch delivers the canonical, server-normalised
   * result, and how a change made in another tab arrives. A DIRTY form keeps
   * what is on screen, which is the whole of the 409 fix: the conflict refetch
   * lands here, finds unsaved work, and leaves it alone.
   *
   * A report still on the card holds the adopt back for one more commit, so the
   * alert is never removed in the same frame that swaps the values under the
   * reader: dropping it re-runs this effect, and the values land next.
   */
  /**
   * A report is announced by being reached, not by being rendered: it carries
   * the only durable account of what happened, and the toast beside it is gone
   * in four seconds. Focus lands on it in the commit that mounts it, so it is
   * read whether or not the reader can see the card, and the Save button it
   * warns about is one Tab away rather than a whole document away.
   */
  React.useEffect(() => {
    if (conflictFields === null) return
    conflictRef.current?.focus()
  }, [conflictFields])

  React.useEffect(() => {
    if (conflictFields !== null) return
    if (signature === appliedRef.current) return
    if (isDirty) return
    appliedRef.current = signature
    syncedRef.current = defaultValues
    form.reset(defaultValues)
  }, [signature, isDirty, defaultValues, form, conflictFields])

  // Returns the promise so a caller can await the refetch before reading the
  // cache. It used to swallow it with `void`, which is why the conflict report
  // could only ever have been computed from stale data.
  const invalidate = React.useCallback(
    () => queryClient.invalidateQueries({ queryKey: SETTINGS_QUERY_KEY }),
    [queryClient]
  )

  const fieldLabel = React.useCallback(
    (field: SystemSettingsField) => {
      const path = field.path ?? ""
      return sectionI18n
        ? t(`systemSettings.${sectionI18n}.${fieldI18nKey(path)}`, {
            defaultValue: path,
          })
        : path
    },
    [sectionI18n, t]
  )

  /**
   * Builds the bar's list and shows it.
   *
   * Taken when the popover opens rather than followed live, which is not a
   * shortcut: the popover holds focus while it is open and closes on the first
   * click or Tab that leaves it, so nothing in the form can move underneath it.
   * It is also the only way to read `syncedRef` at all — a ref may not be read
   * during render, and this is an event.
   *
   * The previous value is `syncedRef`: what the form last agreed with the
   * server about. NOT `baselineValue`, which is the configuration file's value
   * and already has its own line on the row — naming that here would answer a
   * question nobody asked ("what does the file say") in the place the operator
   * asked a different one ("what did I change it FROM").
   *
   * `renderedPaths` orders the list the way the page is ordered, so scanning
   * the list and scanning the section find things in the same order.
   */
  const openChanges = React.useCallback(() => {
    const values = form.getValues()
    const synced = syncedRef.current
    const byPath = new Map(editable.map((field) => [field.path ?? "", field]))
    setChanges(
      renderedPaths
        .map((path) => byPath.get(path))
        .filter((field): field is SystemSettingsField => field !== undefined)
        .filter((field) => Boolean(dirtyFields[formFieldName(field.path ?? "")]))
        .map((field) => {
          const name = formFieldName(field.path ?? "")
          return {
            path: field.path ?? "",
            label: fieldLabel(field),
            previous: renderSettingValue(synced[name], labels),
            current: renderSettingValue(values[name], labels),
          }
        })
    )
    setChangesOpen(true)
  }, [dirtyFields, editable, fieldLabel, form, labels, renderedPaths])

  /**
   * Which settings moved under us: the fields whose value on the server is no
   * longer the value this form last agreed with it about. That is the honest
   * definition — it names what the other person changed, whether or not this
   * operator has also touched it, because saving over it is the loss either way.
   *
   * SETTINGS_QUERY_KEY is this page's own. The platform-settings page declares
   * an unrelated constant of the same NAME keyed `["platform-settings"]`; there
   * is no shared cache entry here.
   */
  const namesChangedOnServer = React.useCallback((): string[] => {
    const fresh = queryClient
      .getQueryData<SystemSettingsDto>(SETTINGS_QUERY_KEY)
      ?.sections?.find((s) => s.key === sectionKey)
    if (!fresh) return []
    const synced = syncedRef.current
    return (fresh.fields ?? [])
      .filter((f) => !f.sensitive && !f.readOnly)
      .filter((f) => {
        const name = formFieldName(f.path ?? "")
        return name in synced && !sameValue(toFormValue(f), synced[name])
      })
      .map(fieldLabel)
  }, [fieldLabel, queryClient, sectionKey])

  /**
   * The labels of the fields in `values` that differ from the last values this
   * form agreed with the server about, filtered by `predicate`.
   *
   * "Agreed with the server about" is not the same as "the operator changed".
   * After a 409 `syncedRef` is deliberately NOT updated while the server has
   * moved on, so the next successful save can report a field the operator never
   * touched. That is the honest reading and it is the one both callers want: a
   * confirmation names every access-critical value this save will write, and a
   * restart count names every restart this save will make necessary — whoever
   * moved the value first.
   */
  const changedNames = React.useCallback(
    (values: FieldValues, predicate: (field: SystemSettingsField) => boolean) =>
      editable
        .filter(predicate)
        .filter((f) => {
          const name = formFieldName(f.path ?? "")
          return !sameValue(values[name], syncedRef.current[name])
        })
        .map(fieldLabel),
    [editable, fieldLabel]
  )

  const handleFailure = React.useCallback(
    async (error: unknown, status?: number) => {
      if (status !== 409) {
        toast.error(getErrorMessage(error))
        requestAnimationFrame(() => saveRef.current?.focus())
        return
      }
      // Refetch so the report can be computed and the badges tell the truth —
      // but the FORM is not touched: the effect above sees a dirty form and
      // leaves every typed value where it is.
      await invalidate()
      const names = namesChangedOnServer()
      // Only over unsaved work. A report is a statement about values that are
      // still on screen and still at risk, the effect above holds the server's
      // values back while one is showing, and every gesture that drops one goes
      // through the form — so a report raised over a clean form (a conflict on
      // a reset, or a save whose edits were undone while it was in flight)
      // would sit there with nothing left that could clear it. The toast still
      // says what happened.
      //
      // Read from the form and NOT from the `isDirty` this callback closed
      // over: the refetch above is a round trip, and the form can go clean
      // across it. That is not hypothetical — it is one click on Cancel while a
      // save is in flight. The closure's answer would then be the answer from
      // before the request, the report would be welded to a form that agrees
      // with the server, and the subscription that drops a report on the
      // dirty→clean edge has already missed its edge.
      const stillDirty = form.formState.isDirty
      if (stillDirty) setConflictFields(names)
      toast.error(
        names.length > 0
          ? t("systemSettings.conflictFields", {
              fields: listOf(i18n.language, names),
            })
          : t("systemSettings.conflict")
      )
      // With a report, the effect that mounts it takes focus. Without one there
      // is nothing durable to reach, so focus goes back to Save.
      if (!stillDirty) requestAnimationFrame(() => saveRef.current?.focus())
    },
    [form, i18n.language, invalidate, namesChangedOnServer, t]
  )

  const save = useMutation({
    mutationFn: async (values: FieldValues) => {
      // The payload is the COMPLETE override set: only fields that differ
      // from the file baseline are included, so clearing a customization is
      // as simple as typing the file value back. Gated fields are read here
      // like any other — gating changes what an operator can TYPE, never what
      // the section sends.
      const overrides: Record<string, unknown> = {}
      for (const field of editable) {
        const name = formFieldName(field.path ?? "")
        // Only fields this form actually holds. `editable` is recomputed from
        // every refetch, while a dirty form deliberately keeps the values it
        // was built with — so a setting the backend started serving mid-edit is
        // in this list and in no rendered row. `toApiValue` would answer for it
        // anyway, inventing "" / false / [] out of `undefined`, diffing that
        // against the real baseline and writing a value nobody typed into a
        // setting nobody saw.
        if (!(name in values)) continue
        const next = toApiValue(field, values[name])
        if (!sameValue(next, normalizedBaseline(field))) {
          setNested(overrides, field.path ?? "", next)
        }
      }

      const { error, response } = await api.PUT(
        "/api/v1/admin/system-settings/{sectionKey}",
        {
          params: { path: { sectionKey } },
          body: { overrides, rowVersion: section.rowVersion ?? null },
        }
      )
      if (error) throw Object.assign(new Error("save failed"), { error, status: response.status })
    },
    onSuccess: (_result, values) => {
      // Clean the moment the server accepts, from the values it accepted —
      // not from a refetch. A refetch may return the same row version (nothing
      // else changed), and a form that stays dirty after a successful save is
      // a form that will warn about leaving a page with nothing left to save.
      const restarts = changedNames(values, (f) => f.restartRequired === true).length

      syncedRef.current = values
      appliedRef.current = JSON.stringify(values)
      form.reset(values)
      setConflictFields(null)
      setPendingValues(null)

      toast.success(
        restarts > 0
          ? t("systemSettings.savedRestartNeeded", { count: restarts })
          : t("systemSettings.saved")
      )
      void invalidate()

      // The sticky bar is now the only Save, and it unmounts the instant the
      // form goes clean — so the element that had focus is removed and focus
      // falls to <body>. Via the high-impact dialog it is worse: Radix restores
      // focus on close to a trigger that no longer exists. rAF puts this after
      // Radix's restore; the card title is a stable landing place that names
      // what was just saved.
      requestAnimationFrame(() => titleRef.current?.focus())
    },
    onError: (failure: { error?: unknown; status?: number }) => {
      setPendingValues(null)
      void handleFailure(failure.error ?? failure, failure.status)
    },
  })

  const unsavedPrompt = useUnsavedChangesPrompt({
    isDirty,
    // Live now that the prompt can be reached mid-save: the sticky bar keeps
    // the sidebar in reach while a PUT is in flight, so the dialog swaps to
    // "save in progress" copy and disables Discard until the request resolves.
    // It was dead code while `isSaving` defaulted to false.
    isSaving: save.isPending,
  })

  const reset = useMutation({
    mutationFn: async () => {
      const { error, response } = await api.POST(
        "/api/v1/admin/system-settings/{sectionKey}/reset",
        { params: { path: { sectionKey } } }
      )
      if (error) throw Object.assign(new Error("reset failed"), { error, status: response.status })
    },
    onSuccess: () => {
      setConfirmReset(false)
      setConflictFields(null)
      // `form.reset(syncedRef.current)`, never a bare `form.reset()`. RHF's
      // no-argument reset restores `_defaultValues`, which is whatever was last
      // passed to `reset(values)` — the operator's own pre-reset values, not the
      // server's. Resetting to the last agreed values makes the form CLEAN,
      // which is the gesture the adopt effect above is waiting for: the refetch
      // then delivers the file values and the effect takes them. The dialog
      // already warned that customized values are removed, so discarding the
      // operator's unsaved edits here is what they asked for.
      form.reset(syncedRef.current)
      toast.success(t("systemSettings.resetDone"))
      void invalidate()
    },
    onError: (failure: { error?: unknown; status?: number }) => {
      setConfirmReset(false)
      void handleFailure(failure.error ?? failure, failure.status)
    },
  })

  const sendTestEmail = useMutation({
    mutationFn: async () => {
      const { error, response } = await api.POST(
        "/api/v1/admin/system-settings/email/test",
        {}
      )
      if (error) throw Object.assign(new Error("test failed"), { error, status: response.status })
    },
    onSuccess: () => toast.success(t("systemSettings.testEmailSent")),
    onError: (failure: { error?: unknown }) =>
      toast.error(getErrorMessage(failure.error ?? failure)),
  })

  const onValid = (values: FieldValues) => {
    // A confirmation on every save is a click-through trainer. It appears only
    // when this particular change set touches a setting that can end the
    // operator's own access — the ones the registry names.
    const risky = changedNames(values, (f) =>
      isHighImpactPath(sectionKey, f.path ?? "")
    )
    if (risky.length > 0) {
      setHighImpactNames(risky)
      setPendingValues(values)
      return
    }
    save.mutate(values)
  }

  /**
   * An invalid submit used to do nothing at all: no request, no message, and
   * the offending row possibly a screen away. Say so, and go to it.
   *
   * `renderedPaths` and not `Object.keys(errors)`: the first error the OPERATOR
   * meets is the first one down the page, and the page's order is not the
   * registry's.
   *
   * A value inside a switched-off category is still validated — gating changes
   * what can be typed, never what gets sent — but its control is inert, so
   * `focusRow` can only mark the row and the operator has no way to correct it
   * where they are standing. The message then names the category whose switch
   * has to go back on, which is the whole of the way out.
   */
  const onInvalid = (errors: FieldErrors<FieldValues>) => {
    const first = renderedPaths.find((path) => formFieldName(path) in errors)
    const governor = first ? gatingSwitches[first] : undefined
    const blockedBy =
      governor && form.getValues(formFieldName(governor.path ?? "")) !== true
        ? governor
        : undefined
    toast.error(
      blockedBy
        ? `${t("systemSettings.invalidSubmit")} ${t(
            "systemSettings.invalidInDisabledCategory",
            { category: fieldLabel(blockedBy) }
          )}`
        : t("systemSettings.invalidSubmit"),
      // The remedy is the only way out of a state the page cannot fix for the
      // operator, and it is two sentences long. The default four seconds is a
      // budget for reading "Nothing was saved"; this one has to survive being
      // read, understood and acted on, in a message that has just sent the eye
      // somewhere else on the page.
      blockedBy ? { duration: 15000 } : undefined
    )
    if (first) focusRow(document.getElementById(settingAnchorId(first)))
  }

  const hasOverrides = Number(section.version ?? 0) > 0
  const title = sectionI18n
    ? t(`systemSettings.${sectionI18n}.title`, { defaultValue: sectionKey })
    : sectionKey
  const description = sectionI18n
    ? t(`systemSettings.${sectionI18n}.description`, { defaultValue: "" })
    : ""

  // Bootstrap sections are information cards: consumed before the database
  // layer exists, so there is nothing to save from here.
  if (!section.editable) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{title}</CardTitle>
          {description ? <CardDescription>{description}</CardDescription> : null}
        </CardHeader>
        <CardContent>
          <FieldGroup>
            {fields.map((field) => (
              <SettingRow
                key={field.path}
                field={field}
                sectionKey={sectionKey}
                sectionI18n={sectionI18n}
              />
            ))}
          </FieldGroup>
        </CardContent>
        <SectionCompanionAction sectionKey={sectionKey} />
      </Card>
    )
  }

  return (
    <>
      <Card>
        {unsavedPrompt}
        <CardHeader>
          {/* `tabIndex={-1}` so a successful save can put focus somewhere real
              when the bar that had it unmounts. Programmatic focus does not
              match `:focus-visible`, so nothing is drawn. */}
          <CardTitle ref={titleRef} tabIndex={-1}>
            {title}
          </CardTitle>
          {description ? <CardDescription>{description}</CardDescription> : null}
        </CardHeader>
        <CardContent>
          {conflictFields ? (
            // `mb-6` is the one per-usage spacing in this subtree, and it is a
            // deliberate exception rather than an oversight: CardContent is a
            // bare `px-(--card-spacing)` block with no gap of its own, so no
            // primitive owns the distance between this alert and the form under
            // it. `id` and not `data-slot`: Alert sets its own data-slot before
            // spreading props, so a caller-supplied one would delete it.
            //
            // `tabIndex={-1}` so the failure that raises it can put focus here.
            // Programmatic focus does not match `:focus-visible`, so nothing is
            // drawn around it.
            <Alert
              ref={conflictRef}
              tabIndex={-1}
              variant="destructive"
              className="mb-6"
              id="settings-conflict"
            >
              <TriangleAlert />
              <AlertTitle>{t("systemSettings.conflictTitle")}</AlertTitle>
              <AlertDescription>
                {conflictFields.length > 0
                  ? t("systemSettings.conflictFields", {
                      fields: listOf(i18n.language, conflictFields),
                    })
                  : t("systemSettings.conflict")}
              </AlertDescription>
            </Alert>
          ) : null}
          <Form {...form}>
            {/* `handleSubmit` is built from the event and not during render.
                Both submit handlers reach `syncedRef` — the values this form
                last agreed with the server about — and handing a ref-reading
                callback to a function call that happens while rendering is
                indistinguishable, to a reader and to the linter alike, from
                reading the ref itself during that render. Building it where it
                is used costs nothing and leaves nothing to judge. */}
            <form
              id={formId}
              onSubmit={(event) => void form.handleSubmit(onValid, onInvalid)(event)}
            >
              {/* Rows are ruled off from one another, so the breathing room
                  lives in the row padding and the group adds no extra gap. */}
              <FieldGroup className="gap-0">
                {/* Blocks in declared order, then whatever no block claimed. The
                    peer gap is 4; a group adds its own `mt-4` on top of it, so a
                    heading gets twice the air of a panel-to-panel seam — more
                    space above a heading than below it, which is what makes it
                    read as heading rather than as another row. */}
                {blocks.length > 0 ? (
                  <div className="flex flex-col gap-4">
                    {blocks.map((entry) =>
                      entry.block.kind === "category" ? (
                        <CategoryPanel
                          key={entry.block.switchPath}
                          sectionKey={sectionKey}
                          sectionI18n={sectionI18n}
                          control={form.control}
                          entry={entry}
                          dirtyOf={dirtyOf}
                        />
                      ) : (
                        <GroupBlockRows
                          key={entry.block.key}
                          sectionKey={sectionKey}
                          sectionI18n={sectionI18n}
                          control={form.control}
                          headingKey={`groups.${entry.block.key}`}
                          fields={entry.fields}
                          dirtyOf={dirtyOf}
                        />
                      )
                    )}
                    {/* Settings no block claimed stay at card level under their
                        own heading. The difference in level is what says they
                        belong to none of the blocks above. */}
                    {general.length > 0 ? (
                      <GroupBlockRows
                        sectionKey={sectionKey}
                        sectionI18n={sectionI18n}
                        control={form.control}
                        headingKey="groups.general"
                        fields={general}
                        dirtyOf={dirtyOf}
                      />
                    ) : null}
                  </div>
                ) : (
                  general.map((field) => (
                    <SettingRow
                      key={field.path}
                      field={field}
                      sectionKey={sectionKey}
                      sectionI18n={sectionI18n}
                      control={form.control}
                      {...dirtyOf(field.path ?? "")}
                    />
                  ))
                )}
                {/* Section operations, not form actions: Save and Cancel live in
                    the bar pinned to the pane below, where they follow the reader
                    down a forty-row section instead of waiting at the bottom of
                    it. The destructive-ish reset stays at the far end so it can
                    never be hit while reaching for something else. */}
                {sectionKey === "Email" || hasOverrides ? (
                  <div className="flex flex-wrap items-center justify-between gap-3 pt-6">
                    <div className="flex flex-wrap items-center gap-3">
                      {sectionKey === "Email" ? (
                        <Button
                          type="button"
                          variant="outline"
                          disabled={sendTestEmail.isPending}
                          onClick={() => sendTestEmail.mutate()}
                        >
                          {sendTestEmail.isPending ? <Spinner data-icon="inline-start" /> : null}
                          {t("systemSettings.sendTestEmail")}
                        </Button>
                      ) : null}
                    </div>
                    {hasOverrides ? (
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() => setConfirmReset(true)}
                      >
                        {t("systemSettings.resetSection")}
                      </Button>
                    ) : null}
                  </div>
                ) : null}
              </FieldGroup>
            </form>
          </Form>
        </CardContent>
        <SectionCompanionAction sectionKey={sectionKey} />
        <ConfirmDialog
          open={confirmReset}
          onOpenChange={setConfirmReset}
          title={t("systemSettings.resetConfirmTitle")}
          description={t("systemSettings.resetConfirmBody")}
          destructive
          loading={reset.isPending}
          onConfirm={() => reset.mutate()}
        />
        <ConfirmDialog
          open={pendingValues !== null}
          onOpenChange={(open) => {
            if (!open) setPendingValues(null)
          }}
          title={t("systemSettings.highImpactConfirmTitle")}
          description={t("systemSettings.highImpactConfirmBody", {
            fields: listOf(i18n.language, highImpactNames),
          })}
          destructive
          loading={save.isPending}
          onConfirm={() => {
            if (pendingValues) save.mutate(pendingValues)
          }}
        />
      </Card>
      {isDirty || save.isPending ? (
        // Sticky against the settings pane, not against the card: a Card is
        // `overflow-hidden`, which makes it a scroll container of its own and
        // pins any `sticky` descendant to a box that never scrolls. As a
        // sibling it sticks to the pane the operator is actually scrolling.
        //
        // `bottom-2`, not `bottom-0`, and the 2 is the pane's own `lg:p-2`.
        // A sticky element's view rectangle is the SCROLLPORT, which for
        // `overflow: auto` is the padding box — and the padding box is also
        // where the pane clips. At `bottom-0` the bar's bottom edge lands
        // exactly on the clip edge, so `shadow-md` (which is entirely
        // downward) is destroyed while the bar is pinned and reappears at the
        // end of the scroll, where the bar also jumps 8px as it un-sticks. At
        // `bottom-2` the pinned position IS the resting position: no jump, and
        // the shadow has the pane's padding to render into.
        //
        // Not a `Card`, deliberately: `overflow-hidden` would kill the sticky
        // and `rounded-4xl` is the wrong radius for a 50px bar. `rounded-xl`
        // is the radius the category panels already use for a bordered strip.
        // Every token here is the preset's; no colour is invented.
        //
        // `role="region"` with the count as its name: the bar is the last node
        // in the document order of a section that can run to forty rows, and
        // sticky puts it permanently on screen — so the reader who cannot see
        // it is the one furthest from it. A landmark is what makes it reachable
        // in one jump instead of one Tab per row.
        <div
          data-slot="settings-unsaved-bar"
          role="region"
          aria-labelledby={`${formId}-unsaved`}
          className="sticky bottom-2 z-10 mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border bg-card px-4 py-3 shadow-md"
        >
          {/* The count is the question "which ones?" asked and not answered, so
              it opens the answer. A Popover and not a Sheet or a Collapsible:
              it is small contextual content on a click, and it portals to the
              top layer, which is how it escapes the bar's own `z-10` without
              anyone hand-picking a stacking order.

              The region's name is still this element — whichever of the two it
              is — so the landmark that makes a forty-row section reachable in
              one jump keeps saying how much is unsaved. */}
          {dirtyCount > 0 ? (
            <Popover
              open={changesOpen}
              onOpenChange={(next) =>
                next ? openChanges() : setChangesOpen(false)
              }
            >
              <PopoverTrigger asChild>
                {/* `-ms-3` gives back the button's own start padding: the count
                    has to sit where it sat as a paragraph, on the bar's edge. */}
                <Button type="button" variant="ghost" size="sm" className="-ms-3">
                  <span id={`${formId}-unsaved`}>
                    {t("systemSettings.unsavedCount", { count: dirtyCount })}
                  </span>
                  {/* What pressing it does, for a reader who cannot see the
                      chevron. In its own element rather than in an `aria-label`,
                      which would REPLACE the visible count — a control whose
                      name does not contain its own visible text is one a voice
                      user cannot ask for, and the region above would then be
                      named after the action instead of the state. */}
                  <span className="sr-only">
                    {t("systemSettings.reviewChanges")}
                  </span>
                  <ChevronDown data-icon="inline-end" />
                </Button>
              </PopoverTrigger>
              <PopoverContent
                align="start"
                // Wider than the primitive's `w-72`: an entry carries a setting
                // label and two values, and the panel is anchored to a bar that
                // spans the whole column, so there is room to spend.
                className="w-96"
                aria-labelledby={`${formId}-changes-title`}
                onCloseAutoFocus={(event) => {
                  const target = jumpTargetRef.current
                  jumpTargetRef.current = null
                  if (!target) return
                  // An entry was pressed to go somewhere, so the row takes the
                  // focus Radix was about to hand back to the trigger — and only
                  // here, where the popover is already gone.
                  event.preventDefault()
                  focusRow(document.getElementById(settingAnchorId(target)))
                }}
              >
                <PopoverHeader>
                  <PopoverTitle id={`${formId}-changes-title`}>
                    {t("systemSettings.changesTitle")}
                  </PopoverTitle>
                </PopoverHeader>
                <UnsavedChangesList
                  changes={changes}
                  onJump={(path) => {
                    jumpTargetRef.current = path
                    setChangesOpen(false)
                  }}
                />
              </PopoverContent>
            </Popover>
          ) : (
            // Nothing to list: the bar is only still here because a save is in
            // flight. The name the region points at has to exist either way.
            <p id={`${formId}-unsaved`} className="text-sm font-medium">
              {t("systemSettings.unsavedCount", { count: dirtyCount })}
            </p>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <Button
              type="button"
              variant="ghost"
              // Not while a save is in flight. Discarding then discards nothing:
              // the request is already on its way and its success resets the
              // form to the values it carried, so the operator would watch
              // their discard undo itself a second later. The navigation prompt
              // says the same thing in the same state.
              disabled={save.isPending}
              onClick={() => {
                // Explicit values, never a bare `form.reset()`. RHF restores
                // `_defaultValues` — the operator's own originals — so after a
                // conflict a bare reset would show THEIR values, not the other
                // person's. Resetting to the last agreed values makes the form
                // clean, and the adopt effect then pulls in what the server
                // actually holds: discard mine, show theirs, one gesture.
                // Clearing the report first lets that same effect adopt on the
                // very next render instead of spending one on the alert.
                setConflictFields(null)
                form.reset(syncedRef.current)
              }}
            >
              {t("common.cancel")}
            </Button>
            {/* `aria-describedby` only while a report stands: pressing this
                does what the report says it does, and "Saving now replaces
                theirs" is not a fact that should reach only the reader who can
                see the top of the card. */}
            <Button
              ref={saveRef}
              type="submit"
              form={formId}
              aria-describedby={conflictFields ? "settings-conflict" : undefined}
              disabled={save.isPending}
            >
              {save.isPending ? <Spinner data-icon="inline-start" /> : null}
              {t("common.save")}
            </Button>
          </div>
        </div>
      ) : null}
    </>
  )
}
