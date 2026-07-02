import { TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { CopyButton } from "@/components/common/copy-button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"

/**
 * One-time reveal of newly generated secret material (API key, webhook key,
 * generated PEM, etc.). The value is shown read-only with a copy action and is
 * never persisted in app state. Closing dismisses it permanently.
 */
export function SecretRevealDialog({
  open,
  onOpenChange,
  title,
  description,
  value,
  multiline = false,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  value: string
  multiline?: boolean
}) {
  const { t } = useTranslation()

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description ? (
            <DialogDescription className="flex items-start gap-2">
              <TriangleAlert className="mt-0.5 size-4 shrink-0 text-destructive" />
              <span>{description}</span>
            </DialogDescription>
          ) : null}
        </DialogHeader>

        {multiline ? (
          <div className="flex flex-col gap-2">
            <Textarea
              readOnly
              value={value}
              rows={8}
              className="font-mono text-xs"
              onFocus={(e) => e.currentTarget.select()}
            />
            <div className="flex justify-end">
              <CopyButton value={value} />
            </div>
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <Input
              readOnly
              value={value}
              className="font-mono text-xs"
              onFocus={(e) => e.currentTarget.select()}
            />
            <CopyButton value={value} />
          </div>
        )}

        <DialogFooter>
          <Button onClick={() => onOpenChange(false)}>
            {t("common.close")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
