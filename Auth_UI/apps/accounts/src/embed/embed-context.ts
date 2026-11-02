import * as React from "react"

import type { AccountBar } from "./embed-api"

/** What every frame of the account bar knows: who it talks to, and the person. */
export interface EmbedContextValue {
  parentOrigin: string
  account: { data: AccountBar | null | undefined; isPending: boolean; isError: boolean; refetch: () => unknown }
}

export const EmbedContext = React.createContext<EmbedContextValue | null>(null)

export function useEmbed(): EmbedContextValue {
  const value = React.useContext(EmbedContext)
  if (!value) throw new Error("useEmbed must be used inside EmbedProviders")
  return value
}
