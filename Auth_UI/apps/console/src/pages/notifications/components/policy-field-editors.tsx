import { Plus, Trash2 } from "lucide-react"
import { useTranslation } from "react-i18next"

import type { PolicySection } from "@astoom/ui/common/policy-document"
import { Button } from "@astoom/ui/button"
import { Card, CardContent } from "@astoom/ui/card"
import { Field, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { Textarea } from "@astoom/ui/textarea"

/**
 * Editor for an ordered list of prose strings (paragraphs or bullets).
 * `dir="auto"` lets each textarea follow the language being edited, so RTL
 * documents stay readable without a per-locale switch.
 */
export function StringListEditor({
  label,
  values,
  onChange,
  disabled,
  rows = 3,
}: {
  label: string
  values: string[]
  onChange: (next: string[]) => void
  disabled?: boolean
  rows?: number
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
              aria-label={t("common.remove")}
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

  const update = (index: number, patch: Partial<PolicySection>) => {
    const next = [...sections]
    next[index] = { ...next[index], ...patch }
    onChange(next)
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
          <CardContent className="flex flex-col gap-4 pt-6">
            <div className="flex items-start gap-2">
              <Field className="flex-1">
                <FieldLabel>{t("notifications.policyHeading")}</FieldLabel>
                <Input
                  dir="auto"
                  value={section.heading}
                  disabled={disabled}
                  onChange={(event) => update(index, { heading: event.target.value })}
                />
              </Field>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="mt-6"
                disabled={disabled}
                aria-label={t("common.remove")}
                onClick={() => onChange(sections.filter((_, i) => i !== index))}
              >
                <Trash2 />
              </Button>
            </div>

            <StringListEditor
              label={t("notifications.policyParagraphs")}
              values={section.paragraphs}
              disabled={disabled}
              onChange={(paragraphs) => update(index, { paragraphs })}
            />

            <StringListEditor
              label={t("notifications.policyBullets")}
              values={section.bullets ?? []}
              disabled={disabled}
              rows={2}
              onChange={(bullets) => update(index, { bullets })}
            />
          </CardContent>
        </Card>
      ))}
    </div>
  )
}
