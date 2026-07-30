import { useTranslation } from "react-i18next"

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@astoom/ui/select"
import { useApplications } from "@astoom/ui/hooks/use-applications"

const ALL_VALUE = "__all__"

/** Application picker backed by the cached applications list. */
export function ApplicationSelect({
  id,
  value,
  onChange,
  allowAll = false,
  placeholder,
  className,
}: {
  /** Trigger id, so a `FieldLabel htmlFor` can name the control. */
  id?: string
  value: string | undefined
  onChange: (value: string | undefined) => void
  allowAll?: boolean
  placeholder?: string
  className?: string
}) {
  const { t } = useTranslation()
  const { data, isLoading } = useApplications()
  const apps = (data?.applications ?? []).filter((app) => Boolean(app.id))
  // With no selectable options, keep the trigger disabled so it never opens an
  // empty popover (an empty Radix Select inside a Dialog can otherwise leak a
  // dismiss that closes the dialog).
  const isEmpty = apps.length === 0 && !allowAll
  // Distinguish "still loading" from "genuinely empty" so the trigger does not
  // briefly read "No applications" before the list arrives.
  const placeholder_ = isLoading
    ? t("common.loading")
    : isEmpty
      ? t("common.noApplications")
      : (placeholder ?? t("common.selectApplication"))

  return (
    <Select
      value={value ?? (allowAll ? ALL_VALUE : undefined)}
      onValueChange={(next) => onChange(next === ALL_VALUE ? undefined : next)}
      disabled={isEmpty || isLoading}
    >
      <SelectTrigger id={id} className={className}>
        <SelectValue placeholder={placeholder_} />
      </SelectTrigger>
      <SelectContent>
        {allowAll ? (
          <SelectItem value={ALL_VALUE}>{t("common.all")}</SelectItem>
        ) : null}
        {apps.map((app) => (
          <SelectItem key={app.id} value={app.id as string}>
            {app.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
