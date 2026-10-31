import type * as React from "react"

/**
 * An application tab's direction line, then its create button, the way the
 * identity-keys tab opens. Shared by every tab of the application page that
 * creates something (roles, permissions, API keys, webhook keys), so they all
 * open the same way. Kept out of the lazy key-tabs module: the roles and
 * permissions tabs are part of the page itself.
 */
export function TabHeader({
  description,
  createButton,
}: {
  description: string
  createButton: React.ReactNode
}) {
  return (
    <>
      <p className="text-sm text-muted-foreground">{description}</p>
      {createButton ? (
        <div className="flex justify-end">{createButton}</div>
      ) : null}
    </>
  )
}
