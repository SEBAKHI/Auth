import { Plus, X } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { getErrorMessage } from "@astoom/api/errors"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { DatePicker, monthsFromNow } from "@astoom/ui/common/date-picker"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { Field, FieldGroup, FieldLabel } from "@astoom/ui/field"
import { useDirtyClose } from "@astoom/ui/hooks/use-dirty-close"
import { Skeleton } from "@astoom/ui/skeleton"

/** One assignment as it exists on the server. */
export interface AssignmentItem {
  /** Identity used both to de-duplicate the picker and to address the removal. */
  key: string
  label: string
}

interface StagedAddition<TDraft> extends AssignmentItem {
  draft: TDraft
}

/** What the picker slot receives to build its options and stage an addition. */
export interface AssignmentPickerContext<TDraft> {
  /**
   * Keys currently assigned *as the user sees them* — server truth with the
   * staged removals taken out and the staged additions put in. Filter the
   * picker's options with this, never with the raw server list.
   */
  assignedKeys: Set<string>
  add: (item: StagedAddition<TDraft>) => void
}

/**
 * Editor for a list of assignments (roles, permissions, app roles) where adding
 * and removing are staged locally and applied together.
 *
 * Nothing reaches the API until the user saves: chips are added and removed in
 * place, `Save` asks for confirmation with a summary of what will change, and
 * closing with pending edits warns before discarding them. That makes an
 * accidental click on a chip's ✕ harmless, which it was not while every click
 * fired its own request.
 *
 * Additions and removals are applied one by one and independently: a single
 * rejected change (a role deleted by someone else, a permission conflict) leaves
 * the others applied, stays staged, and is reported — the dialog then shows
 * server truth again rather than a stale optimistic list.
 */
export function AssignmentDialog<TDraft>({
  open,
  onOpenChange,
  title,
  description,
  items,
  loading = false,
  emptyLabel,
  assignedLabel,
  picker,
  onAdd,
  onRemove,
  onApplied,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: React.ReactNode
  description?: React.ReactNode
  /** Current assignments from the server. */
  items: AssignmentItem[]
  loading?: boolean
  /** Shown when nothing is assigned. */
  emptyLabel: string
  /** Label above the chip list. */
  assignedLabel: string
  picker: (context: AssignmentPickerContext<TDraft>) => React.ReactNode
  /** Creates one assignment. Rejections are surfaced, not swallowed. */
  onAdd: (draft: TDraft) => Promise<void>
  /** Deletes one assignment by its `key`. */
  onRemove: (key: string) => Promise<void>
  /** Invalidate the queries backing `items` — called after every apply. */
  onApplied: () => void
}) {
  const { t } = useTranslation()
  const [added, setAdded] = React.useState<StagedAddition<TDraft>[]>([])
  const [removed, setRemoved] = React.useState<string[]>([])
  const [confirmOpen, setConfirmOpen] = React.useState(false)
  const [applying, setApplying] = React.useState(false)

  // Reopening always starts from server truth; a previous session's staged
  // edits must never leak into the next one.
  React.useEffect(() => {
    if (open) {
      setAdded([])
      setRemoved([])
    }
  }, [open])

  const isDirty = added.length > 0 || removed.length > 0
  const { requestOpenChange, discardDialog } = useDirtyClose({
    isDirty,
    onOpenChange,
  })

  const kept = items.filter((item) => !removed.includes(item.key))
  const assignedKeys = new Set([
    ...kept.map((item) => item.key),
    ...added.map((item) => item.key),
  ])

  const add = (item: StagedAddition<TDraft>) => {
    // Re-picking something that is staged for removal just cancels the removal:
    // no pointless DELETE + INSERT round-trip, and the original assignment
    // (including its expiry) is kept intact.
    if (removed.includes(item.key)) {
      setRemoved((keys) => keys.filter((key) => key !== item.key))
      return
    }
    if (assignedKeys.has(item.key)) return
    setAdded((current) => [...current, item])
  }

  const stageRemoval = (key: string) => setRemoved((keys) => [...keys, key])

  const unstageAddition = (key: string) =>
    setAdded((current) => current.filter((item) => item.key !== key))

  const apply = async () => {
    setApplying(true)

    const failures: unknown[] = []
    const stillRemoved: string[] = []
    const stillAdded: StagedAddition<TDraft>[] = []

    // Removals first: freeing an assignment before re-creating it keeps the
    // unique constraint on the assignment tables satisfied in every order.
    for (const key of removed) {
      try {
        await onRemove(key)
      } catch (error) {
        failures.push(error)
        stillRemoved.push(key)
      }
    }

    for (const item of added) {
      try {
        await onAdd(item.draft)
      } catch (error) {
        failures.push(error)
        stillAdded.push(item)
      }
    }

    onApplied()
    setConfirmOpen(false)
    setApplying(false)

    if (failures.length > 0) {
      setRemoved(stillRemoved)
      setAdded(stillAdded)
      toast.error(getErrorMessage(failures[0]))
      return
    }

    setRemoved([])
    setAdded([])
    toast.success(t("common.changesSaved"))
    // Bypass the dirty guard: the changes are saved, not discarded.
    onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={requestOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description ? (
            <DialogDescription>{description}</DialogDescription>
          ) : null}
        </DialogHeader>

        <FieldGroup>
          {picker({ assignedKeys, add })}

          <Field>
            <FieldLabel>{assignedLabel}</FieldLabel>
            <div className="min-h-24 rounded-lg border p-3">
              {loading ? (
                <div className="flex flex-wrap gap-2">
                  {Array.from({ length: 3 }).map((_, index) => (
                    <Skeleton key={index} className="h-6 w-24" />
                  ))}
                </div>
              ) : kept.length === 0 && added.length === 0 ? (
                <p className="py-4 text-center text-sm text-muted-foreground">
                  {emptyLabel}
                </p>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {kept.map((item) => (
                    <AssignmentChip
                      key={item.key}
                      label={item.label}
                      onRemove={() => stageRemoval(item.key)}
                    />
                  ))}
                  {added.map((item) => (
                    <AssignmentChip
                      key={item.key}
                      label={item.label}
                      pending
                      onRemove={() => unstageAddition(item.key)}
                    />
                  ))}
                </div>
              )}
            </div>
          </Field>
        </FieldGroup>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => requestOpenChange(false)}
            disabled={applying}
          >
            {t("common.cancel")}
          </Button>
          <Button onClick={() => setConfirmOpen(true)} disabled={!isDirty}>
            {t("common.saveWithCount", { count: added.length + removed.length })}
          </Button>
        </DialogFooter>

        {discardDialog}

        <ConfirmDialog
          open={confirmOpen}
          onOpenChange={setConfirmOpen}
          title={t("common.applyChangesTitle")}
          confirmLabel={t("common.save")}
          loading={applying}
          onConfirm={() => void apply()}
        >
          <ChangeSummary
            addedLabels={added.map((item) => item.label)}
            removedLabels={items
              .filter((item) => removed.includes(item.key))
              .map((item) => item.label)}
          />
        </ConfirmDialog>
      </DialogContent>
    </Dialog>
  )
}

function AssignmentChip({
  label,
  pending = false,
  onRemove,
}: {
  label: string
  pending?: boolean
  onRemove: () => void
}) {
  const { t } = useTranslation()

  return (
    <Badge variant={pending ? "outline" : "secondary"} className="gap-1 pe-1">
      {label}
      {pending ? (
        <span className="sr-only">{t("common.pending")}</span>
      ) : null}
      <button
        type="button"
        className="rounded-full p-0.5 hover:bg-foreground/10"
        aria-label={t("common.remove")}
        onClick={onRemove}
      >
        <X className="size-3" />
      </button>
    </Badge>
  )
}

/** The "+ these, − those" preview shown in the save confirmation. */
function ChangeSummary({
  addedLabels,
  removedLabels,
}: {
  addedLabels: string[]
  removedLabels: string[]
}) {
  const { t } = useTranslation()

  return (
    <div className="flex max-h-56 flex-col gap-3 overflow-y-auto text-sm">
      {addedLabels.length > 0 ? (
        <ChangeList
          title={t("common.toBeAdded", { count: addedLabels.length })}
          labels={addedLabels}
          icon={<Plus className="size-3 shrink-0" />}
        />
      ) : null}
      {removedLabels.length > 0 ? (
        <ChangeList
          title={t("common.toBeRemoved", { count: removedLabels.length })}
          labels={removedLabels}
          icon={<X className="size-3 shrink-0" />}
        />
      ) : null}
    </div>
  )
}

function ChangeList({
  title,
  labels,
  icon,
}: {
  title: string
  labels: string[]
  icon: React.ReactNode
}) {
  return (
    <div className="flex flex-col gap-1 text-start">
      <p className="font-medium">{title}</p>
      <ul className="flex flex-col gap-1 text-muted-foreground">
        {labels.map((label) => (
          <li key={label} className="flex items-center gap-2">
            {icon}
            <span className="min-w-0 break-words">{label}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

/**
 * Shared picker row for `AssignmentDialog`: the caller supplies the selects
 * (one for a role, two when an application has to be chosen first) and this
 * owns the optional expiry and the add button.
 *
 * Everything stacks by default and only pairs up once the dialog is wide
 * enough — the old fixed-width single row was what pushed these dialogs into a
 * horizontal scrollbar, especially with longer Arabic labels.
 */
export function AssignmentPicker({
  addLabel,
  canAdd,
  onAdd,
  expiresAt,
  onExpiresAtChange,
  children,
}: {
  addLabel: string
  canAdd: boolean
  onAdd: () => void
  expiresAt: string
  onExpiresAtChange: (value: string) => void
  children: React.ReactNode
}) {
  const { t } = useTranslation()

  return (
    <Field>
      {children}
      <Field orientation="responsive">
        <DatePicker
          value={expiresAt}
          onChange={(value) => onExpiresAtChange(value ?? "")}
          minDate={new Date()}
          maxDate={monthsFromNow(10)}
          placeholder={t("common.expiresAt")}
        />
        <Button onClick={onAdd} disabled={!canAdd}>
          <Plus data-icon="inline-start" />
          {addLabel}
        </Button>
      </Field>
    </Field>
  )
}
