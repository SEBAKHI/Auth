import { useTranslation } from "react-i18next"

import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { useApplications } from "@authsystem/ui/hooks/use-applications"

const ALL_VALUE = "__all__"

/** Application picker backed by the cached applications list. */
export function ApplicationSelect({
  id,
  value,
  onChange,
  allowAll = false,
  placeholder,
  className,
  options,
  loading,
}: {
  /** Trigger id, so a `FieldLabel htmlFor` can name the control. */
  id?: string
  value: string | undefined
  onChange: (value: string | undefined) => void
  allowAll?: boolean
  placeholder?: string
  className?: string
  /**
   * Explicit list, replacing the platform-wide one.
   *
   * Callers that need a narrower set pass it here rather than filtering
   * downstream: this component is shared by nine screens whose needs differ —
   * an organization may only enable applications open to everyone, while the
   * role, permission and API-key screens must be able to reach every one.
   */
  options?: { id?: string; name?: string }[]
  /** Loading flag for a caller-supplied list. */
  loading?: boolean
}) {
  const { t } = useTranslation()
  const shared = useApplications()
  const source = options ?? shared.data?.applications ?? []
  const isLoading = options ? Boolean(loading) : shared.isLoading
  const apps = source.filter((app) => Boolean(app.id))
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
        <SelectGroup>
          {allowAll ? (
            <SelectItem value={ALL_VALUE}>{t("common.all")}</SelectItem>
          ) : null}
          {apps.map((app) => (
            <SelectItem key={app.id} value={app.id as string}>
              {app.name}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  )
}
