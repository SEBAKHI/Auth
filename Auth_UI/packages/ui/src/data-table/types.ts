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
    /**
     * The record fields this column already puts on screen.
     *
     * Auto-discovery can only see a column's `id` and `accessorKey`, so a
     * column that reads its value through an `accessorFn` declares nothing
     * about what it consumed. Every column whose id is a CONCEPT — `actor`
     * reading `performedByEmail`, `status` reading `isActive`, `name` reading
     * three name fields — therefore left its own sources looking untouched,
     * and they came back as extra columns showing the same thing again under
     * an untranslated heading.
     *
     * List a field when the cell actually PUTS IT ON SCREEN. Naming a field
     * the API does not return is harmless; naming one the cell does not show
     * deletes the only column that could have shown it. Two cases decide most
     * of the judgement calls:
     *
     * - A conditional fallback (`modifiedAt ?? createdAt`) shows the second
     *   field only on rows where the first is missing. Do NOT cover it — on
     *   every other row it would be nowhere.
     * - A raw id is covered only when its resolved name is itself
     *   auto-discovered, because the pairing in `nameSiblingKey` then makes the
     *   id's column render that name a second time. When the name is a curated
     *   column's own id, the pairing never fires and the id's column shows the
     *   GUID — a different fact, so leave it alone.
     */
    covers?: readonly string[]
    /**
     * Start this column hidden, leaving it in the column-visibility menu.
     *
     * For a column that belongs to the record but is redundant on one surface:
     * a table of one user's audit trail already names that user above it, so a
     * `subject` column repeats the same person on every row. The column still
     * has to EXIST there — it is what `covers` declares, and deleting it would
     * bring the three fields it hides back as auto columns — so "not shown" and
     * "not defined" are different answers and only the first one is right.
     *
     * A default, not a rule: the reader's own choice is persisted per table and
     * wins over this on every later visit.
     */
    defaultHidden?: boolean
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
