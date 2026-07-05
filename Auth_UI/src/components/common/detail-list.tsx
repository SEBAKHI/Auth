import type * as React from "react"

import { Card, CardContent } from "@/components/ui/card"
import { cn } from "@/lib/utils"

export interface DetailItem {
  label: string
  value: React.ReactNode
  /** Span the full grid width (e.g. long descriptions). */
  fullWidth?: boolean
}

/**
 * Read-only label/value grid inside a Card, rendered between a detail page's
 * header and its tabs. Items with empty values are skipped.
 */
export function DetailList({ items }: { items: DetailItem[] }) {
  const visible = items.filter(
    (item) =>
      item.value !== null && item.value !== undefined && item.value !== ""
  )
  if (visible.length === 0) return null

  return (
    <Card size="sm">
      <CardContent>
        <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2 lg:grid-cols-3">
          {visible.map((item) => (
            <div
              key={item.label}
              className={cn(
                "min-w-0",
                item.fullWidth && "sm:col-span-2 lg:col-span-3"
              )}
            >
              <dt className="text-xs text-muted-foreground">{item.label}</dt>
              <dd className="text-sm break-words">{item.value}</dd>
            </div>
          ))}
        </dl>
      </CardContent>
    </Card>
  )
}
