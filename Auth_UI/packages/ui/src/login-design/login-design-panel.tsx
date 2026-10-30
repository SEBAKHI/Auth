import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"

import { API_BASE_URL } from "@authsystem/api/env"
import type { Schemas } from "@authsystem/api/types"
import type { LoginDesignRef } from "@authsystem/ui/branding"
import { DesignFrame, type DesignFrameState } from "@authsystem/ui/login-design/design-frame"
import { useResolvedTheme } from "@authsystem/ui/theme-provider"

type PublicLoginDesign = Schemas["PublicLoginDesignDto"]

/**
 * The rendered design of a sign-in page, for the page's language. Anonymous: the page asks
 * before anyone signs in, and only when its branding named a design. Thrown, not answered
 * with null, on any failure: the panel stays plain, and the next screen asks again.
 */
async function fetchLoginDesign(
  clientId: string | null,
  language: string,
  signal: AbortSignal
): Promise<PublicLoginDesign> {
  const path = clientId
    ? `/api/v1/applications/${encodeURIComponent(clientId)}/login-design`
    : "/api/v1/Platform/login-design"
  const res = await fetch(`${API_BASE_URL}${path}?lang=${encodeURIComponent(language)}`, { signal })
  if (!res.ok) throw new Error(`login-design ${res.status}`)
  return (await res.json()) as PublicLoginDesign
}

/**
 * The panel half of a sign-in page when a design is active: the design, rendered by the
 * server, in its isolated frame, following the page's appearance and mode.
 *
 * `AuthLayout` mounts it only on a screen wide enough to show the panel and only when the
 * branding names a design. The request for the design and the frame's own page start
 * together, at the page's first render: the frame used to wait for the design, and the
 * design for this component's own chunk.
 */
export default function LoginDesignPanel({
  clientId,
  design,
  onStateChange,
}: {
  /** The application of the page's pending request; null on the platform's own pages. */
  clientId: string | null
  design: LoginDesignRef
  onStateChange: (state: DesignFrameState) => void
}) {
  const { t, i18n } = useTranslation()
  const mode = useResolvedTheme()
  const language = i18n.resolvedLanguage ?? i18n.language

  // Keyed by the version too: a design saved since the branding was cached is a new
  // entry, and the old copy is never shown under the new version.
  const query = useQuery({
    queryKey: ["login-design", clientId, design.id, design.contentHash, language],
    queryFn: ({ signal }) => fetchLoginDesign(clientId, language, signal),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })

  // A failed render leaves the plain panel; the frame waiting for it goes too.
  if (query.isError) return null

  return (
    <DesignFrame
      html={query.data?.html ?? null}
      lang={query.data?.language ?? language}
      dir={query.data?.dir === "rtl" ? "rtl" : "ltr"}
      mode={mode === "dark" ? "dark" : "light"}
      title={t("auth.loginDesignTitle")}
      onStateChange={onStateChange}
    />
  )
}
