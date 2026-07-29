import { Plus, Trash2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import type { PolicySection } from "@astoom/ui/common/policy-document"
import { Button } from "@astoom/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@astoom/ui/card"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Field, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { Separator } from "@astoom/ui/separator"
import { Textarea } from "@astoom/ui/textarea"

/*
 * List items are keyed by index on purpose. The document is plain JSON with no
 * stable ids, and immutable updates replace the object on every keystroke — an
 * identity-based key would remount the field (and drop focus) on every letter
 * typed. Every input here is fully controlled, so index keys render correctly.
 */

/**
 * Editor for an ordered list of prose strings (paragraphs or bullets).
 * `dir="auto"` lets each textarea follow the language being edited, so RTL
 * documents stay readable while typing.
 */
export function StringListEditor({
  label,
  values,
  onChange,
  disabled,
  rows = 3,
  removeLabel,
}: {
  label: string
  values: string[]
  onChange: (next: string[]) => void
  disabled?: boolean
  rows?: number
  /** Accessible name for each item's remove button. */
  removeLabel?: string
}) {
  const { t } = useTranslation()

  return (
    <Field>
      <FieldLabel>{label}</FieldLabel>
      <div className="flex flex-col gap-2">
        {values.map((value, index) => (
          <div key={index} className="flex items-start gap-2">
            <Textarea
              dir="auto"
              rows={rows}
              className="flex-1"
              value={value}
              disabled={disabled}
              onChange={(event) => {
                const next = [...values]
                next[index] = event.target.value
                onChange(next)
              }}
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              disabled={disabled}
              aria-label={removeLabel ?? t("common.remove")}
              title={removeLabel ?? t("common.remove")}
              onClick={() => onChange(values.filter((_, i) => i !== index))}
            >
              <Trash2 />
            </Button>
          </div>
        ))}
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="w-fit"
          disabled={disabled}
          onClick={() => onChange([...values, ""])}
        >
          <Plus data-icon="inline-start" />
          {t("common.create")}
        </Button>
      </div>
    </Field>
  )
}

/**
 * Editor for a list of policy sections (heading + paragraphs + bullets), used
 * for the main body, the jurisdiction rights blocks and the closing blocks.
 *
 * Each section owns a titled card with an explicit "Remove section" button:
 * deleting a whole block of legal text is a destructive act, so it gets a
 * labelled control and a confirmation rather than a bare icon that reads as
 * "clear this field".
 */
export function SectionListEditor({
  label,
  sections,
  onChange,
  disabled,
}: {
  label: string
  sections: PolicySection[]
  onChange: (next: PolicySection[]) => void
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const [pendingRemoval, setPendingRemoval] = React.useState<number | null>(null)

  const update = (index: number, patch: Partial<PolicySection>) => {
    const next = [...sections]
    next[index] = { ...next[index], ...patch }
    onChange(next)
  }

  const remove = (index: number) =>
    onChange(sections.filter((_, i) => i !== index))

  /** Empty sections vanish without a prompt; written ones must be confirmed. */
  const requestRemove = (index: number) => {
    const section = sections[index]
    const hasContent =
      section.heading.trim().length > 0 ||
      section.paragraphs.some((p) => p.trim().length > 0) ||
      (section.bullets ?? []).some((b) => b.trim().length > 0)

    if (hasContent) setPendingRemoval(index)
    else remove(index)
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">{label}</span>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() =>
            onChange([...sections, { heading: "", paragraphs: [""], bullets: [] }])
          }
        >
          <Plus data-icon="inline-start" />
          {t("notifications.policyAddSection")}
        </Button>
      </div>

      {sections.map((section, index) => (
        <Card key={index}>
          <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0">
            <CardTitle className="text-sm text-muted-foreground">
              {t("notifications.policySectionNumber", { number: index + 1 })}
            </CardTitle>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="text-destructive hover:text-destructive"
              disabled={disabled}
              onClick={() => requestRemove(index)}
            >
              <Trash2 data-icon="inline-start" />
              {t("notifications.policyRemoveSection")}
            </Button>
          </CardHeader>
          <Separator />
          <CardContent className="flex flex-col gap-4 pt-4">
            <Field>
              <FieldLabel>{t("notifications.policyHeading")}</FieldLabel>
              <Input
                dir="auto"
                value={section.heading}
                disabled={disabled}
                onChange={(event) => update(index, { heading: event.target.value })}
              />
            </Field>

            <StringListEditor
              label={t("notifications.policyParagraphs")}
              values={section.paragraphs}
              disabled={disabled}
              removeLabel={t("notifications.policyRemoveParagraph")}
              onChange={(paragraphs) => update(index, { paragraphs })}
            />

            <StringListEditor
              label={t("notifications.policyBullets")}
              values={section.bullets ?? []}
              disabled={disabled}
              rows={2}
              removeLabel={t("notifications.policyRemoveBullet")}
              onChange={(bullets) => update(index, { bullets })}
            />
          </CardContent>
        </Card>
      ))}

      <ConfirmDialog
        open={pendingRemoval !== null}
        onOpenChange={(open) => {
          if (!open) setPendingRemoval(null)
        }}
        title={t("notifications.policyRemoveSectionTitle")}
        description={t("notifications.policyRemoveSectionBody")}
        confirmLabel={t("notifications.policyRemoveSection")}
        destructive
        onConfirm={() => {
          if (pendingRemoval !== null) remove(pendingRemoval)
          setPendingRemoval(null)
        }}
      />
    </div>
  )
}
