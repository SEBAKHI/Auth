import * as React from "react"
import { Pencil } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@/components/ui/button"
import {
  Field,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@/components/ui/field"
import { ScrollArea } from "@/components/ui/scroll-area"
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet"
import {
  DEFAULT_AUDIT_FIELD_KEYS,
  formatFieldValue,
  humanizeKey,
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
  const side = i18n.dir() === "rtl" ? "left" : "right"

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
    const mainFields: Array<[string, unknown]> = []
    const auditFields: Array<[string, unknown]> = []
    if (row && typeof row === "object") {
      const auditOrder = auditFieldKeys as readonly string[]
      const auditSet = new Set(auditOrder)
      const hiddenSet = new Set(hiddenKeys ?? [])
      for (const [key, value] of Object.entries(row)) {
        if (hiddenSet.has(key)) continue
        if (auditSet.has(key)) auditFields.push([key, value])
        else mainFields.push([key, value])
      }
      auditFields.sort(
        (a, b) => auditOrder.indexOf(a[0]) - auditOrder.indexOf(b[0])
      )
    }
    return { main: mainFields, audit: auditFields }
  }, [row, auditFieldKeys, hiddenKeys])

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      {row ? (
        <SheetContent side={side} className="w-full sm:max-w-md">
          <SheetHeader>
            <SheetTitle>{title ?? t("common.details")}</SheetTitle>
            <SheetDescription className="sr-only">
              {t("common.details")}
            </SheetDescription>
          </SheetHeader>

          <ScrollArea className="flex-1">
            <FieldGroup className="px-6 pb-6">
              {main.map(([key, value]) => (
                <FieldRow
                  key={key}
                  label={resolveLabel(key)}
                  value={formatFieldValue(value, t)}
                />
              ))}

              {audit.length > 0 ? (
                <FieldSet>
                  <FieldLegend variant="label">
                    {t("common.auditFields")}
                  </FieldLegend>
                  {audit.map(([key, value]) => (
                    <FieldRow
                      key={key}
                      label={resolveLabel(key)}
                      value={formatFieldValue(value, t)}
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
                <Pencil />
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
