import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { uploadImage } from "@astoom/api/upload"
import { getErrorMessage } from "@astoom/api/errors"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { Button } from "@astoom/ui/button"
import { Input } from "@astoom/ui/input"
import { Label } from "@astoom/ui/label"

/**
 * Uploads an image through the shared image endpoint (returns an absolute URL so
 * it loads in email clients — inline base64 is blocked by most of them) and
 * inserts a responsive <img> tag at the editor cursor. Width may be pixels or a
 * percentage; max-width keeps it from overflowing narrow email columns.
 */
export function InsertImageDialog({
  open,
  onOpenChange,
  onInsert,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  onInsert: (snippet: string) => void
}) {
  const { t } = useTranslation()
  const [url, setUrl] = React.useState<string | null>(null)
  const [uploading, setUploading] = React.useState(false)
  const [width, setWidth] = React.useState("")
  const [alt, setAlt] = React.useState("")
  const fileInputRef = React.useRef<HTMLInputElement>(null)

  const reset = () => {
    setUrl(null)
    setWidth("")
    setAlt("")
    setUploading(false)
  }

  const handleFile = async (file: File | undefined) => {
    if (!file) return
    setUploading(true)
    try {
      const result = await uploadImage(file)
      setUrl(result.url)
    } catch (error) {
      toast.error(getErrorMessage(error))
    } finally {
      setUploading(false)
    }
  }

  const buildSnippet = (): string => {
    // Normalize a bare number to px; pass through values that already have a unit or %.
    const normalizedWidth = width.trim()
    const widthAttr =
      normalizedWidth === ""
        ? ""
        : /^\d+$/.test(normalizedWidth)
          ? ` width="${normalizedWidth}"`
          : ""
    const widthStyle = normalizedWidth === "" ? "" : `width:${/^\d+$/.test(normalizedWidth) ? `${normalizedWidth}px` : normalizedWidth};`
    const altAttr = alt.trim() ? ` alt="${alt.replace(/"/g, "&quot;")}"` : ' alt=""'
    return `<img src="${url}"${altAttr}${widthAttr} style="${widthStyle}max-width:100%;height:auto;" />`
  }

  const insert = () => {
    if (!url) return
    onInsert(buildSnippet())
    toast.success(t("notifications.imageInserted"))
    onOpenChange(false)
    reset()
  }

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        if (!next) reset()
      }}
      title={t("notifications.insertImageTitle")}
      description={t("notifications.insertImageHint")}
      confirmLabel={t("notifications.insertImageAction")}
      loading={uploading}
      onConfirm={insert}
    >
      <div className="space-y-4">
        <input
          ref={fileInputRef}
          type="file"
          accept="image/png,image/jpeg,image/gif,image/webp"
          className="hidden"
          onChange={(e) => void handleFile(e.target.files?.[0])}
        />

        <Button
          type="button"
          variant="outline"
          disabled={uploading}
          onClick={() => fileInputRef.current?.click()}
        >
          {uploading ? t("notifications.uploadingImage") : t("notifications.chooseImage")}
        </Button>

        {url ? (
          <div className="space-y-4">
            <div className="rounded-md border bg-muted/30 p-3">
              <img src={url} alt="" className="mx-auto max-h-40 max-w-full rounded" />
            </div>

            <div className="space-y-2">
              <Label htmlFor="insert-image-width">{t("notifications.imageWidth")}</Label>
              <Input
                id="insert-image-width"
                dir="ltr"
                value={width}
                onChange={(e) => setWidth(e.target.value)}
                placeholder="320  ·  60%"
              />
              <p className="text-xs text-muted-foreground">{t("notifications.imageWidthHint")}</p>
            </div>

            <div className="space-y-2">
              <Label htmlFor="insert-image-alt">{t("notifications.imageAlt")}</Label>
              <Input
                id="insert-image-alt"
                dir="auto"
                value={alt}
                onChange={(e) => setAlt(e.target.value)}
              />
              <p className="text-xs text-muted-foreground">{t("notifications.imageAltHint")}</p>
            </div>
          </div>
        ) : null}
      </div>
    </ConfirmDialog>
  )
}
