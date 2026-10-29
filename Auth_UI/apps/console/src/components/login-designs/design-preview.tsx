import * as React from "react"
import { useTranslation } from "react-i18next"

import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { useAdoptedStyleSheet } from "@authsystem/ui/hooks/use-adopted-style-sheet"
import { DesignFrame, type DesignFrameState } from "@authsystem/ui/login-design/design-frame"
import { Skeleton } from "@authsystem/ui/skeleton"
import { buildThemeVars, themeCss } from "@authsystem/ui/theme/build-theme"
import { themeConfigKey, toThemeConfig, type ThemeConfig } from "@authsystem/ui/theme/theme-config"

import {
  PreviewSchemeToggle,
  type PreviewScheme,
} from "@/pages/notifications/components/email-preview-frame"
import { usePreviewScheme } from "@/pages/notifications/components/use-preview-scheme"

/** The scope the preview's appearance stylesheet applies to (build-theme `themeCss`). */
const PREVIEW_SCOPE = "login-design-preview"

/**
 * A design as the sign-in pages will show it: the server's rendering of the editor's
 * content, in the same isolated frame, following the appearance the pages will use (an
 * application's own while it is switched on, the platform's otherwise) in the mode the
 * toggle chooses, whatever mode the console itself is in.
 */
export function DesignPreview({
  html,
  lang,
  dir,
  appearance,
  error,
}: {
  /** The rendered design; null while the first render is on its way. */
  html: string | null
  lang: string
  dir: "ltr" | "rtl"
  appearance: ThemeConfig | null
  error: string | null
}) {
  const { t } = useTranslation()
  const [scheme, setScheme] = usePreviewScheme()
  const probe = React.useRef<HTMLDivElement>(null)
  const [state, setState] = React.useState<DesignFrameState>("pending")
  const appearanceKey = appearance ? themeConfigKey(appearance) : "platform"

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-medium">{t("loginDesigns.preview")}</p>
        <PreviewSchemeToggle scheme={scheme} onSchemeChange={(next: PreviewScheme) => setScheme(next)} />
      </div>

      {/* The element the design's tokens are read from: the appearance in the chosen
          mode, scoped so the console itself keeps its own colours. */}
      {appearance ? <ScopedAppearance config={appearance} /> : null}
      <div data-appearance-scope={appearance ? PREVIEW_SCOPE : undefined} hidden>
        <div ref={probe} className={scheme} />
      </div>

      {error ? (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      ) : null}

      <div
        className="relative h-[560px] overflow-hidden rounded-md border bg-muted"
        data-design-state={html === null ? "pending" : state}
      >
        {html === null ? <Skeleton className="size-full" /> : null}
        {html !== null ? (
          <DesignFrame
            html={html}
            lang={lang}
            dir={dir}
            mode={scheme}
            tokensFrom={() => probe.current}
            appearanceKey={`${appearanceKey}|${scheme}`}
            title={t("loginDesigns.previewTitle")}
            onStateChange={setState}
          />
        ) : null}
        {state === "failed" && html !== null ? (
          <p className="absolute inset-x-0 top-1/2 px-4 text-center text-sm text-muted-foreground">
            {t("loginDesigns.previewFailed")}
          </p>
        ) : null}
      </div>
    </div>
  )
}

/** The appearance being previewed, for the inside of the preview's scope alone. */
function ScopedAppearance({ config }: { config: ThemeConfig }) {
  const key = themeConfigKey(config)
  const css = React.useMemo(
    () => themeCss(buildThemeVars(toThemeConfig(JSON.parse(key))), PREVIEW_SCOPE),
    [key]
  )
  useAdoptedStyleSheet(css)
  return null
}
