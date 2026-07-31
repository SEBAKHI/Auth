import { Search, X } from "lucide-react"
import { useTranslation } from "react-i18next"

import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from "@astoom/ui/input-group"
import { cn } from "@astoom/ui/utils"

/**
 * The project's single search field.
 *
 * Replaces the hand-rolled `relative` wrapper + absolutely positioned icon +
 * `ps-8` padded `Input` that was duplicated across the list pages: `InputGroup`
 * owns the affordance, so the icon spacing and focus ring stay consistent and the
 * layout is RTL-correct without per-usage offsets. A clear button appears once
 * there is text, so resetting a filter is one click instead of a select-and-delete.
 */
export function SearchInput({
  value,
  onChange,
  placeholder,
  className,
  id,
  autoFocus,
}: {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  className?: string
  id?: string
  autoFocus?: boolean
}) {
  const { t } = useTranslation()

  return (
    <InputGroup className={cn("max-w-sm", className)}>
      <InputGroupAddon>
        <Search />
      </InputGroupAddon>
      <InputGroupInput
        id={id}
        type="search"
        autoFocus={autoFocus}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder ?? t("common.search")}
      />
      {value ? (
        <InputGroupAddon align="inline-end">
          <InputGroupButton
            size="icon-xs"
            aria-label={t("common.clear")}
            onClick={() => onChange("")}
          >
            <X />
          </InputGroupButton>
        </InputGroupAddon>
      ) : null}
    </InputGroup>
  )
}
