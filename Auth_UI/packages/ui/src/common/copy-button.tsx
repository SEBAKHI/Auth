import * as React from "react"
import { Check, Copy } from "lucide-react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { Button } from "@authsystem/ui/button"

/** Icon button that copies a value to the clipboard with feedback. */
export function CopyButton({
  value,
  label,
}: {
  value: string
  label?: string
}) {
  const { t } = useTranslation()
  const [copied, setCopied] = React.useState(false)

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      toast.success(t("common.copied"))
      window.setTimeout(() => setCopied(false), 1500)
    } catch {
      toast.error(t("common.error"))
    }
  }

  return (
    <Button
      type="button"
      variant="outline"
      size="icon-sm"
      onClick={handleCopy}
      aria-label={label ?? t("common.copy")}
    >
      {copied ? <Check /> : <Copy />}
    </Button>
  )
}
