import type { Table } from "@tanstack/react-table"
import { SlidersHorizontal } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"

interface DataTableViewOptionsProps<TData> {
  table: Table<TData>
}

/**
 * The columns a user may hide. Exported so the toolbar can decide whether it has
 * anything to render without duplicating the rule.
 */
export function getHideableColumns<TData>(table: Table<TData>) {
  return table
    .getAllColumns()
    .filter(
      (column) =>
        typeof column.accessorFn !== "undefined" && column.getCanHide()
    )
}

/**
 * The "Columns" button rendered at the end of the toolbar. Lists every column
 * that may be hidden (`getCanHide()`) as a checkbox so users toggle visibility.
 * Labels come from `columnDef.meta.label`, falling back to the column id, so the
 * menu stays stable across language switches.
 */
export function DataTableViewOptions<TData>({
  table,
}: DataTableViewOptionsProps<TData>) {
  const { t } = useTranslation()

  const hideableColumns = getHideableColumns(table)

  if (hideableColumns.length === 0) return null

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" aria-label={t("common.toggleColumns")}>
          <SlidersHorizontal data-icon="inline-start" />
          <span className="hidden sm:inline">{t("common.columns")}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-44">
        <DropdownMenuLabel>{t("common.toggleColumns")}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          {hideableColumns.map((column) => (
            <DropdownMenuCheckboxItem
              key={column.id}
              checked={column.getIsVisible()}
              onCheckedChange={(value) => column.toggleVisibility(!!value)}
              onSelect={(event) => event.preventDefault()}
            >
              {column.columnDef.meta?.label ?? column.id}
            </DropdownMenuCheckboxItem>
          ))}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
