import * as React from "react"
import { ShieldCheck } from "lucide-react"

import { cn } from "@authsystem/ui/utils"

/**
 * The heights, in CSS pixels, a logo may be given on the sign-in pages: the area the logo takes
 * on the page, never the image file. The API's copy is `Auth.Domain/Constants/LogoHeights.cs`,
 * which validates them; `logo-mark.test.tsx` reads that file and holds the two together.
 */
export const LOGO_HEIGHT = {
  min: 24,
  max: 120,
  /** The platform's logo while no height is chosen: 1.5 × the 40px it had before. */
  platformDefault: 60,
  /** An application's logo while no height is chosen: the 48px it always had. */
  applicationDefault: 48,
} as const

/**
 * How many times wider than tall a logo's box may grow, per owner. A cap tied to the height, not
 * a fixed width, so a wide wordmark grows and shrinks with its height instead of stopping at a
 * width. Each keeps the proportion its box had before heights could be chosen: the platform's
 * 160×40 (so 240px at the 60px default, the 1.5 × 160px of the owner's decision), an
 * application's 240×48.
 */
export const LOGO_MAX_ASPECT = { platform: 4, application: 5 } as const

/**
 * The height a logo is drawn at: the stored one, or `fallback` while none is chosen. A stored
 * value outside the range (an older API, a cached copy) reads as none chosen, as the API reads it.
 */
export function resolveLogoHeight(stored: number | string | null | undefined, fallback: number): number {
  // The generated contract types every integer as `number | string`.
  const px = typeof stored === "string" ? Number(stored) : stored
  return typeof px === "number" && Number.isInteger(px) && px >= LOGO_HEIGHT.min && px <= LOGO_HEIGHT.max
    ? px
    : fallback
}

/** The box's size, as the variables its classes read: the height, and the width cap it implies. */
function boxStyle(heightPx: number, maxAspect: number): React.CSSProperties {
  return {
    "--logo-h": `${heightPx}px`,
    "--logo-w-max": `${heightPx * maxAspect}px`,
  } as React.CSSProperties
}

/**
 * A logo at a given height. The width follows the image's own proportions up to `maxAspect` ×
 * the height (its owner's {@link LOGO_MAX_ASPECT}), and never past its column: a narrower column (375px)
 * shrinks the drawing inside the box (`object-contain`), so it is never stretched, cropped or
 * pushed out of the page.
 *
 * The one place that decides how a logo box is sized: every sign-in page draws both logos
 * through it (`AuthLayout`), and the console's preview draws them the same way.
 */
export function LogoMark({
  src,
  alt,
  heightPx,
  maxAspect,
  onError,
  className,
}: {
  src: string
  alt: string
  heightPx: number
  maxAspect: number
  onError?: () => void
  className?: string
}) {
  return (
    <img
      data-slot="logo-mark"
      src={src}
      alt={alt}
      onError={onError}
      style={boxStyle(heightPx, maxAspect)}
      className={cn("h-(--logo-h) w-auto max-w-[min(var(--logo-w-max),100%)] object-contain", className)}
    />
  )
}

/**
 * The platform's mark in a page's corner: its logo, or, when none is uploaded, the default
 * shield tile with the platform's name beside it, at the same height.
 *
 * While the branding is still unknown neither is drawn: the fallback is a statement ("this
 * platform has no logo"), and asserting it before the answer arrives made the default shield
 * flash on every cold load. An empty box of the same height holds the place meanwhile.
 */
export function PlatformMark({
  src,
  name,
  heightPx,
  pending = false,
}: {
  src: string | null
  name: string
  heightPx: number
  pending?: boolean
}) {
  if (pending) {
    return <span aria-hidden data-slot="logo-mark" style={boxStyle(heightPx, LOGO_MAX_ASPECT.platform)} className="block h-(--logo-h)" />
  }
  if (src) {
    return <LogoMark src={src} alt={name} heightPx={heightPx} maxAspect={LOGO_MAX_ASPECT.platform} />
  }
  return (
    <span className="flex min-w-0 items-center gap-2">
      <span
        data-slot="logo-mark"
        style={boxStyle(heightPx, LOGO_MAX_ASPECT.platform)}
        className="flex size-(--logo-h) shrink-0 items-center justify-center rounded-lg bg-primary text-primary-foreground"
      >
        <ShieldCheck className="size-[calc(var(--logo-h)/2)]" />
      </span>
      <LogoName name={name} />
    </span>
  )
}

/**
 * A name beside or in place of a logo, cut with an ellipsis when it does not fit.
 *
 * The ellipsis falls at the end of the cutting box in that box's own direction: inheriting an
 * RTL page's direction, a Latin name lost its beginning instead of its end ("… Everyone
 * Everywhere"). Its own direction keeps the start in view in every language.
 */
export function LogoName({ name }: { name: string }) {
  return (
    <bdi dir="auto" className="min-w-0 truncate text-sm font-semibold">
      {name}
    </bdi>
  )
}
