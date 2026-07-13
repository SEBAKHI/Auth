import { QRCodeSVG } from "qrcode.react"

import { cn } from "@astoom/ui/utils"

/**
 * Renders a QR code for the given value. The white padded backdrop is part of
 * the QR spec (quiet zone) and keeps codes scannable on dark backgrounds.
 */
export function QrCode({
  value,
  size = 192,
  className,
}: {
  value: string
  size?: number
  className?: string
}) {
  return (
    <div className={cn("w-fit rounded-lg bg-white p-3", className)}>
      <QRCodeSVG value={value} size={size} level="M" marginSize={0} />
    </div>
  )
}
