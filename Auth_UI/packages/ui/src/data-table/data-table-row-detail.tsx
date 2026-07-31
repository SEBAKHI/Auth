import * as React from "react"
import { Pencil } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import {
  Field,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@astoom/ui/field"
import { ScrollArea } from "@astoom/ui/scroll-area"
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from "@astoom/ui/sheet"
import { directionForLanguage } from "@astoom/i18n"
import {
  DEFAULT_AUDIT_FIELD_KEYS,
  formatFieldValue,
  humanizeKey,
  nameSiblingKey,
  pairedLabelKey,
} from "./field-format"

interface DataTableRowDetailProps<TData> {
  /** The row record to display; the panel renders nothing when null. */
  row: TData | null
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Field name → localized label, derived from the table's columns. */
  labelMap?: Record<string, string>
  /** Field names rendered under the "Audit Fields" group at the bottom. */
  auditFieldKeys?: readonly string[]
  /** Field names hidden from the panel entirely. */
  hiddenKeys?: readonly string[]
  /** When provided, an Edit button appears in the footer. */
  onEdit?: (row: TData) => void
  /** Panel title; defaults to `common.details`. */
  title?: string
}

function FieldRow({ label, value }: { label: string; value: string }) {
  return (
    <Field>
      <FieldLabel className="text-xs font-normal text-muted-foreground">
        {label}
      </FieldLabel>
      <span className="text-sm break-words">{value}</span>
    </Field>
  )
}

/**
 * Generic detail panel shared by every table: opens as a side `Sheet` and lists
 * all fields of the clicked record using shadcn `Field` primitives. Verification
 * fields (created/modified by/at) are pulled into a trailing "Audit Fields"
 * group. An optional Edit button hands the record back to the page's own form.
 */
export function DataTableRowDetail<TData>({
  row,
  open,
  onOpenChange,
  labelMap,
  auditFieldKeys = DEFAULT_AUDIT_FIELD_KEYS,
  hiddenKeys,
  onEdit,
  title,
}: DataTableRowDetailProps<TData>) {
  const { t, i18n } = useTranslation()
  // `Sheet` takes a physical side, so derive it from the same direction source the
  // document does. `i18n.dir()` reports `ltr` on a cold Arabic load (see
  // `initI18n`), which opened this panel from the wrong edge.
  const side = directionForLanguage(i18n.language) === "rtl" ? "left" : "right"

  const resolveLabel = React.useCallback(
    (key: string): string => {
      if (labelMap?.[key]) return labelMap[key]
      const commonKey = `common.${key}`
      if (i18n.exists(commonKey)) return t(commonKey)
      return humanizeKey(key)
    },
    [labelMap, i18n, t]
  )

  const { main, audit } = React.useMemo(() => {
    type DetailField = { key: string; value: unknown; paired: boolean }
    const mainFields: DetailField[] = []
    const auditFields: DetailField[] = []
    if (row && typeof row === "object") {
      const record = row as Record<string, unknown>
      const auditOrder = auditFieldKeys as readonly string[]
      const auditSet = new Set(auditOrder)
      const hiddenSet = new Set(hiddenKeys ?? [])
      const keySet = new Set(Object.keys(record))
      // Resolved-name siblings collapse into their id field (applicationId +
      // applicationName, createdBy + createdByName): the id row shows the
      // name and the sibling row is dropped as a duplicate.
      const consumed = new Set<string>()
      for (const key of keySet) {
        const sibling = nameSiblingKey(key)
        if (sibling !== key && keySet.has(sibling)) consumed.add(sibling)
      }
      for (const [key, value] of Object.entries(record)) {
        if (hiddenSet.has(key) || consumed.has(key)) continue
        const paired = consumed.has(nameSiblingKey(key))
        const name = paired ? record[nameSiblingKey(key)] : undefined
        const display =
          typeof name === "string" && name !== "" ? name : value
        const entry: DetailField = { key, value: display, paired }
        if (auditSet.has(key)) auditFields.push(entry)
        else mainFields.push(entry)
      }
      auditFields.sort(
        (a, b) => auditOrder.indexOf(a.key) - auditOrder.indexOf(b.key)
      )
    }
    return { main: mainFields, audit: auditFields }
  }, [row, auditFieldKeys, hiddenKeys])

  // Paired id fields drop the "Id" suffix from their label ("Application id"
  // → "Application"); everything else keeps the table/i18n label.
  const detailLabel = React.useCallback(
    (field: { key: string; paired: boolean }): string =>
      field.paired && field.key.endsWith("Id")
        ? humanizeKey(pairedLabelKey(field.key))
        : resolveLabel(field.key),
    [resolveLabel]
  )

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      {row ? (
        <SheetContent side={side}>
          <SheetHeader>
            <SheetTitle>{title ?? t("common.details")}</SheetTitle>
            <SheetDescription className="sr-only">
              {t("common.details")}
            </SheetDescription>
          </SheetHeader>

          <ScrollArea className="min-h-0 flex-1">
            <FieldGroup className="px-6 pb-6">
              {main.map((field) => (
                <FieldRow
                  key={field.key}
                  label={detailLabel(field)}
                  value={formatFieldValue(field.value, t)}
                />
              ))}

              {audit.length > 0 ? (
                <FieldSet>
                  <FieldLegend variant="label">
                    {t("common.auditFields")}
                  </FieldLegend>
                  {audit.map((field) => (
                    <FieldRow
                      key={field.key}
                      label={detailLabel(field)}
                      value={formatFieldValue(field.value, t)}
                    />
                  ))}
                </FieldSet>
              ) : null}
            </FieldGroup>
          </ScrollArea>

          <SheetFooter className="flex-row justify-end gap-2">
            {onEdit ? (
              <Button
                onClick={() => {
                  onOpenChange(false)
                  onEdit(row)
                }}
              >
                <Pencil data-icon="inline-start" />
                {t("common.edit")}
              </Button>
            ) : null}
            <SheetClose asChild>
              <Button variant="outline">{t("common.close")}</Button>
            </SheetClose>
          </SheetFooter>
        </SheetContent>
      ) : null}
    </Sheet>
  )
}
