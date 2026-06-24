import {
  flexRender,
  getCoreRowModel,
  useReactTable,
  type ColumnDef,
} from "@tanstack/react-table"
import { ChevronLeft, ChevronRight } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { getErrorMessage } from "@/lib/errors"

const PAGE_SIZES = [10, 20, 50, 100]

export interface DataTablePagination {
  pageIndex: number
  pageSize: number
  pageCount: number
  totalCount?: number
  onPageChange: (pageIndex: number) => void
  onPageSizeChange: (pageSize: number) => void
}

interface DataTableProps<TData> {
  columns: ColumnDef<TData, unknown>[]
  data: TData[]
  isLoading?: boolean
  error?: unknown
  onRetry?: () => void
  emptyMessage?: string
  /** Provide for server-paginated tables; omit for in-place arrays. */
  pagination?: DataTablePagination
}

/**
 * Data table built on TanStack Table + shadcn primitives. Renders skeletons
 * while loading, an error state with retry, and an empty state so every list
 * screen behaves consistently. Server pagination is opt-in via `pagination`.
 */
export function DataTable<TData>({
  columns,
  data,
  isLoading = false,
  error,
  onRetry,
  emptyMessage,
  pagination,
}: DataTableProps<TData>) {
  const { t } = useTranslation()

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
    manualPagination: Boolean(pagination),
    pageCount: pagination ? Math.max(pagination.pageCount, 1) : undefined,
    state: pagination
      ? {
          pagination: {
            pageIndex: pagination.pageIndex,
            pageSize: pagination.pageSize,
          },
        }
      : undefined,
  })

  const columnCount = columns.length
  const rows = table.getRowModel().rows

  return (
    <div className="space-y-3">
      <div className="overflow-hidden rounded-lg border">
        <Table>
          <TableHeader>
            {table.getHeaderGroups().map((group) => (
              <TableRow key={group.id}>
                {group.headers.map((header) => (
                  <TableHead key={header.id}>
                    {header.isPlaceholder
                      ? null
                      : flexRender(
                          header.column.columnDef.header,
                          header.getContext()
                        )}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {isLoading ? (
              Array.from({ length: 6 }).map((_, rowIdx) => (
                <TableRow key={`skeleton-${rowIdx}`}>
                  {Array.from({ length: columnCount }).map((__, cellIdx) => (
                    <TableCell key={`skeleton-${rowIdx}-${cellIdx}`}>
                      <Skeleton className="h-5 w-full" />
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : error ? (
              <TableRow>
                <TableCell colSpan={columnCount} className="h-32 text-center">
                  <div className="flex flex-col items-center gap-2">
                    <p className="text-sm text-muted-foreground">
                      {getErrorMessage(error, t("common.error"))}
                    </p>
                    {onRetry ? (
                      <Button variant="outline" size="sm" onClick={onRetry}>
                        {t("common.retry")}
                      </Button>
                    ) : null}
                  </div>
                </TableCell>
              </TableRow>
            ) : rows.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={columnCount}
                  className="h-32 text-center text-sm text-muted-foreground"
                >
                  {emptyMessage ?? t("common.noResults")}
                </TableCell>
              </TableRow>
            ) : (
              rows.map((row) => (
                <TableRow key={row.id}>
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(
                        cell.column.columnDef.cell,
                        cell.getContext()
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {pagination ? (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-sm text-muted-foreground">
            {typeof pagination.totalCount === "number"
              ? t("common.showing", {
                  count: data.length,
                  total: pagination.totalCount,
                })
              : null}
          </p>
          <div className="flex items-center gap-2">
            <Select
              value={String(pagination.pageSize)}
              onValueChange={(value) =>
                pagination.onPageSizeChange(Number(value))
              }
            >
              <SelectTrigger size="sm" className="w-[120px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PAGE_SIZES.map((size) => (
                  <SelectItem key={size} value={String(size)}>
                    {size} / {t("common.page")}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <span className="px-1 text-sm text-muted-foreground">
              {t("common.pageOf", {
                page: pagination.pageIndex + 1,
                total: Math.max(pagination.pageCount, 1),
              })}
            </span>
            <Button
              variant="outline"
              size="icon-sm"
              aria-label={t("common.previous")}
              disabled={pagination.pageIndex <= 0 || isLoading}
              onClick={() => pagination.onPageChange(pagination.pageIndex - 1)}
            >
              <ChevronLeft className="rtl:rotate-180" />
            </Button>
            <Button
              variant="outline"
              size="icon-sm"
              aria-label={t("common.next")}
              disabled={
                pagination.pageIndex + 1 >= pagination.pageCount || isLoading
              }
              onClick={() => pagination.onPageChange(pagination.pageIndex + 1)}
            >
              <ChevronRight className="rtl:rotate-180" />
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  )
}
