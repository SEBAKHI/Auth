import type * as React from "react"

/** Consistent page title block with an optional leading slot (e.g. avatar) and actions area. */
export function PageHeader({
  title,
  description,
  actions,
  leading,
}: {
  title: string
  description?: string
  actions?: React.ReactNode
  leading?: React.ReactNode
}) {
  return (
    <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
      <div className="flex min-w-0 items-center gap-3">
        {leading}
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {description ? (
            <p className="text-sm text-muted-foreground">{description}</p>
          ) : null}
        </div>
      </div>
      {actions ? (
        <div className="flex w-full min-w-0 flex-wrap items-center gap-2 xl:w-auto xl:flex-1 xl:justify-end">
          {actions}
        </div>
      ) : null}
    </div>
  )
}
