import type { FilterFn, RowData } from "@tanstack/react-table"

/**
 * Module augmentation that lets any `ColumnDef` describe how the shared
 * {@link DataTable} should treat it — without the page touching the table
 * component itself. Every property is optional, so existing columns keep
 * working untouched.
 */
declare module "@tanstack/react-table" {
  // The signature must mirror the library's own to merge correctly; the type
  // parameters are required by that signature even though we don't use them.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData extends RowData, TValue> {
    /** Human label shown in the column-visibility menu and faceted filter. */
    label?: string
    /** Opt the column into a toolbar filter. `faceted` renders a multi-select. */
    filterVariant?: "text" | "faceted"
    /** Explicit options for a faceted filter; derived from data when omitted. */
    filterOptions?: { label: string; value: string }[]
    /** Exclude this column's field from the CSV export. */
    excludeFromExport?: boolean
    /** Hide this column's field from the row-detail panel. */
    detailHidden?: boolean
    /** Pin the column in place; it can neither be dragged nor moved by menu. */
    enableReordering?: boolean
  }

  /** Registers the shared faceted (multi-select) filter under a string key. */
  interface FilterFns {
    faceted: FilterFn<unknown>
  }
}

export {}
