import type { Table } from "@tanstack/react-table"
import { ChevronDown, ChevronUp, SlidersHorizontal } from "lucide-react"
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

import { ACTIONS_COLUMN_ID } from "./column-order"

interface DataTableViewOptionsProps<TData> {
  table: Table<TData>
  /** Move a column `delta` slots in the display order. Omit to disable moving. */
  onMoveColumn?: (columnId: string, delta: number) => void
}

/**
 * The columns a user may hide, in the order they are displayed.
 *
 * Exported so the toolbar can decide whether it has anything to render without
 * duplicating the rule. `getAllColumns()` returns definition order, not display
 * order, so the menu is sorted explicitly — otherwise moving a column would
 * reorder the grid while the menu appeared not to change.
 */
export function getHideableColumns<TData>(table: Table<TData>) {
  const hideable = table
    .getAllColumns()
    .filter(
      (column) =>
        typeof column.accessorFn !== "undefined" && column.getCanHide()
    )

  const order = table.getState().columnOrder
  if (!order || order.length === 0) return hideable

  return [...hideable].sort(
    (a, b) => order.indexOf(a.id) - order.indexOf(b.id)
  )
}

/**
 * The "Columns" button rendered at the end of the toolbar. Lists every column
 * that may be hidden (`getCanHide()`) as a checkbox so users toggle visibility,
 * and — when the table supports reordering — gives each one a pair of move
 * buttons. Labels come from `columnDef.meta.label`, falling back to the column
 * id, so the menu stays stable across language switches.
 *
 * Up/down rather than left/right on purpose: the menu is a vertical list, so the
 * control reads the same under RTL with no mirroring. The move buttons sit
 * outside the menu's roving tab order, so `Alt` + arrow on the focused row is
 * the keyboard equivalent; either way `DataTable` announces the result.
 */
export function DataTableViewOptions<TData>({
  table,
  onMoveColumn,
}: DataTableViewOptionsProps<TData>) {
  const { t } = useTranslation()

  const hideableColumns = getHideableColumns(table)

  if (hideableColumns.length === 0) return null

  // Positions come from the *full* display order, not from this menu's list:
  // display-only columns (an avatar cell, say) are not hideable and so never
  // appear here, yet a move still has to be able to cross them.
  const movable = table
    .getState()
    .columnOrder.filter((id) => id !== ACTIONS_COLUMN_ID)

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" aria-label={t("common.toggleColumns")}>
          <SlidersHorizontal data-icon="inline-start" />
          <span className="hidden sm:inline">{t("common.columns")}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-64">
        <DropdownMenuLabel>{t("common.toggleColumns")}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          {hideableColumns.map((column) => {
            const label = column.columnDef.meta?.label ?? column.id
            const position = movable.indexOf(column.id)
            const canMove =
              Boolean(onMoveColumn) &&
              movable.length > 1 &&
              position !== -1 &&
              column.columnDef.meta?.enableReordering !== false

            return (
              <DropdownMenuCheckboxItem
                key={column.id}
                checked={column.getIsVisible()}
                onCheckedChange={(value) => column.toggleVisibility(!!value)}
                onSelect={(event) => event.preventDefault()}
                aria-keyshortcuts={
                  canMove ? "Alt+ArrowUp Alt+ArrowDown" : undefined
                }
                onKeyDown={(event) => {
                  if (!canMove || !event.altKey) return
                  if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return
                  event.preventDefault()
                  onMoveColumn?.(column.id, event.key === "ArrowUp" ? -1 : 1)
                }}
              >
                <span className="truncate">{label}</span>
                {canMove ? (
                  <span className="ms-auto flex items-center">
                    <Button
                      variant="ghost"
                      size="icon-xs"
                      type="button"
                      tabIndex={-1}
                      disabled={position === 0}
                      aria-label={t("common.moveColumnUp", { column: label })}
                      onClick={(event) => {
                        event.preventDefault()
                        event.stopPropagation()
                        onMoveColumn?.(column.id, -1)
                      }}
                    >
                      <ChevronUp />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-xs"
                      type="button"
                      tabIndex={-1}
                      disabled={position === movable.length - 1}
                      aria-label={t("common.moveColumnDown", { column: label })}
                      onClick={(event) => {
                        event.preventDefault()
                        event.stopPropagation()
                        onMoveColumn?.(column.id, 1)
                      }}
                    >
                      <ChevronDown />
                    </Button>
                  </span>
                ) : null}
              </DropdownMenuCheckboxItem>
            )
          })}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
