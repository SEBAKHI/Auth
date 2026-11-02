import { useQuery } from "@tanstack/react-query"
import * as React from "react"

import { applyLanguage, hasStoredLanguage } from "@authsystem/i18n"
import { DirectionProvider } from "@authsystem/i18n/direction"
import { isLanguageCode } from "@authsystem/i18n/languages"
import { isTheme, ThemeProvider, type Theme } from "@authsystem/ui/theme-provider"

import { EmbedContext, type EmbedContextValue } from "./embed-context"
import { fetchAccountBar } from "./embed-api"
import { onHostMessage, oneOf, THEMES, type HostTheme } from "./protocol"

/*
 * What every frame of the account bar shares: who it talks to, the person, and how it
 * looks. Its look is the accounts app's: the platform's appearance and fonts, and the
 * person's language and colour mode as this browser stores them for the accounts app (a
 * linked application on a subdomain of the IAM site is same-site, so the frame reads the
 * accounts app's own storage). Where this browser stores none, the profile's. Where the
 * application says which mode it shows (its `theme` attribute), that one, so a dark icon is
 * never drawn on its light header. Nothing is ever stored from here.
 */

const ACCOUNT_BAR_QUERY_KEY = ["account-bar"] as const

function storedThemeIsSet(): boolean {
  try {
    return isTheme(localStorage.getItem("theme"))
  } catch {
    return false
  }
}

/** The frames' shared state and look; `bootEmbed` (embed-boot.tsx) mounts it. */
export function EmbedProviders({ parentOrigin, children }: { parentOrigin: string; children: React.ReactNode }) {
  const [hostTheme, setHostTheme] = React.useState<HostTheme | null>(() =>
    oneOf(THEMES, new URLSearchParams(window.location.search).get("theme"))
  )
  React.useEffect(
    () =>
      onHostMessage(parentOrigin, (message) => {
        if (message.type === "theme") setHostTheme(oneOf(THEMES, message.value))
      }),
    [parentOrigin]
  )

  const query = useQuery({
    queryKey: ACCOUNT_BAR_QUERY_KEY,
    queryFn: ({ signal }) => fetchAccountBar(signal),
  })

  // The profile's language, when this browser has none of its own: shown, not stored.
  const profileLanguage = query.data?.preferredLanguage ?? null
  React.useEffect(() => {
    if (profileLanguage && isLanguageCode(profileLanguage) && !hasStoredLanguage()) {
      void applyLanguage(profileLanguage)
    }
  }, [profileLanguage])

  const profileTheme = query.data?.theme ?? null
  const forcedTheme: Theme | undefined =
    hostTheme ?? (!storedThemeIsSet() && isTheme(profileTheme) ? profileTheme : undefined)

  const value = React.useMemo<EmbedContextValue>(
    () => ({
      parentOrigin,
      account: {
        data: query.data,
        isPending: query.isPending,
        isError: query.isError,
        refetch: query.refetch,
      },
    }),
    [parentOrigin, query.data, query.isPending, query.isError, query.refetch]
  )

  return (
    <ThemeProvider forcedTheme={forcedTheme ?? undefined}>
      <DirectionProvider>
        <EmbedContext.Provider value={value}>{children}</EmbedContext.Provider>
      </DirectionProvider>
    </ThemeProvider>
  )
}
