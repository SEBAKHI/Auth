import type * as React from "react"
import { Link } from "react-router-dom"

import { cn } from "@authsystem/ui/utils"

/**
 * A record's name, pointing at that record's own page.
 *
 * This is a real `<a href>`, so the browser can do what a person expects of an
 * address: middle-click or Ctrl/Cmd-click opens it in a second tab, the context
 * menu offers "copy link", and the target shows in the status bar. A command -
 * edit, delete, open a dialog - stays a button; only a destination is a link.
 *
 * `href` is optional because a row does not always have somewhere to go: a
 * deleted record has no detail route, and the generated API types mark every id
 * optional. Rather than repeat that decision at twenty call sites, a row with
 * no destination renders the same content as plain text. The fallback keeps the
 * layout classes on purpose - the column must not shift depending on whether a
 * particular row happens to be linkable.
 */
export function RecordLink({
  href,
  className,
  children,
}: {
  href: string | undefined
  className?: string
  children: React.ReactNode
}) {
  if (!href) {
    return <div className={cn("min-w-0", className)}>{children}</div>
  }

  return (
    <Link to={href} className={cn("min-w-0", className)}>
      {children}
    </Link>
  )
}
