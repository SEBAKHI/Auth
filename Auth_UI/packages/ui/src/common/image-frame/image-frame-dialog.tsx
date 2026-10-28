import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { Field, FieldGroup, FieldTitle } from "@authsystem/ui/field"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Slider } from "@authsystem/ui/slider"
import { Spinner } from "@authsystem/ui/spinner"
import { cn } from "@authsystem/ui/utils"

import {
  MAX_MARGIN,
  MAX_ZOOM,
  clampMargin,
  defaultState,
  isSquareMode,
  isUntouched,
  moveEdge,
  outputRect,
  outputSize,
  panCrop,
  setZoom,
  stageLayout,
  zoomOf,
  type Edge,
  type FrameMode,
  type FrameState,
  type Rect,
  type Size,
  type StageLayout,
} from "./frame"
import { decodeImage, exportFrame, previewUrl } from "./render"

/**
 * The frame editor that sits between choosing an image and uploading it.
 *
 * Loaded on demand (`AvatarMenu` imports it lazily): nothing of it is fetched
 * until a file has been chosen, so the sign-in pages carry none of it.
 *
 * Mounted once per chosen file (the caller keys it), so its state is seeded
 * from that file and never reset by an effect.
 */

type Decoded =
  | { status: "decoding" }
  | { status: "failed" }
  | { status: "ready"; bitmap: ImageBitmap; url: string; image: Size }

/** The stage is a square of this many SVG units, whatever its pixel size. */
const STAGE = 100
const PADDING = 8
const EDGES: readonly Edge[] = ["top", "right", "bottom", "left"]

export default function ImageFrameDialog({
  file,
  mode,
  onCancel,
  onConfirm,
}: {
  file: File
  mode: FrameMode
  onCancel: () => void
  onConfirm: (file: File) => void
}) {
  const { t } = useTranslation()
  const [decoded, setDecoded] = React.useState<Decoded>({ status: "decoding" })
  const [frame, setFrame] = React.useState<FrameState | null>(null)
  const [saving, setSaving] = React.useState(false)
  const savingRef = React.useRef(false)
  const zoomLabelId = React.useId()
  const marginLabelId = React.useId()

  // Decoding a file is work outside React; the bitmap is released when the
  // dialog goes away, whichever of the two finishes first.
  React.useEffect(() => {
    let cancelled = false
    let bitmap: ImageBitmap | null = null
    void decodeImage(file).then((result) => {
      bitmap = result
      if (cancelled) {
        result?.close()
        return
      }
      const url = result ? previewUrl(result) : null
      if (!result || !url) {
        setDecoded({ status: "failed" })
        return
      }
      const image = { width: result.width, height: result.height }
      setFrame(defaultState(image, mode))
      setDecoded({ status: "ready", bitmap: result, url, image })
    })
    return () => {
      cancelled = true
      bitmap?.close()
    }
  }, [file, mode])

  const ready = decoded.status === "ready" ? decoded : null
  const untouched =
    !ready || !frame || isUntouched(frame, ready.image, mode)

  const save = async () => {
    // A double click lands before `saving` re-renders the button disabled.
    if (savingRef.current) return
    savingRef.current = true
    setSaving(true)
    try {
      const result =
        ready && frame
          ? await exportFrame({
              file,
              bitmap: ready.bitmap,
              image: ready.image,
              mode,
              state: frame,
            })
          : file
      onConfirm(result)
    } catch {
      toast.error(t("imageFrame.exportFailed"))
      savingRef.current = false
      setSaving(false)
    }
  }

  const size = frame ? outputSize(frame) : null

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !savingRef.current) onCancel()
      }}
    >
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{t("imageFrame.title")}</DialogTitle>
          <DialogDescription>{t(`imageFrame.description.${mode}`)}</DialogDescription>
        </DialogHeader>

        {file.type === "image/gif" ? (
          <Alert>
            <AlertTitle>{t("imageFrame.gifTitle")}</AlertTitle>
            <AlertDescription>{t("imageFrame.gifBody")}</AlertDescription>
          </Alert>
        ) : null}

        {decoded.status === "failed" ? (
          <Alert>
            <AlertTitle>{t("imageFrame.decodeFailedTitle")}</AlertTitle>
            <AlertDescription>{t("imageFrame.decodeFailedBody")}</AlertDescription>
          </Alert>
        ) : null}

        {decoded.status === "decoding" ? (
          <Skeleton className="mx-auto aspect-square w-full max-w-80" />
        ) : null}

        {ready && frame ? (
          <>
            <FrameStage
              url={ready.url}
              image={ready.image}
              mode={mode}
              frame={frame}
              onFrame={setFrame}
            />
            <FieldGroup>
              <Field>
                <div className="flex items-center justify-between gap-2">
                  <FieldTitle id={zoomLabelId}>{t("imageFrame.zoom")}</FieldTitle>
                  <span className="text-sm text-muted-foreground tabular-nums">
                    {t("imageFrame.percent", {
                      value: Math.round(zoomOf(frame.crop, ready.image, mode) * 100),
                    })}
                  </span>
                </div>
                <Slider
                  aria-labelledby={zoomLabelId}
                  min={100}
                  max={MAX_ZOOM * 100}
                  step={1}
                  value={[Math.round(zoomOf(frame.crop, ready.image, mode) * 100)]}
                  onValueChange={([value]) =>
                    setFrame((current) =>
                      current && {
                        ...current,
                        crop: setZoom(current.crop, value / 100, ready.image, mode),
                      }
                    )
                  }
                />
              </Field>
              <Field>
                <div className="flex items-center justify-between gap-2">
                  <FieldTitle id={marginLabelId}>{t("imageFrame.margin")}</FieldTitle>
                  <span className="text-sm text-muted-foreground tabular-nums">
                    {t("imageFrame.percent", { value: Math.round(frame.margin * 100) })}
                  </span>
                </div>
                <Slider
                  aria-labelledby={marginLabelId}
                  min={0}
                  max={MAX_MARGIN * 100}
                  step={1}
                  value={[Math.round(frame.margin * 100)]}
                  onValueChange={([value]) =>
                    setFrame((current) =>
                      current && { ...current, margin: clampMargin(value / 100) }
                    )
                  }
                />
              </Field>
            </FieldGroup>
          </>
        ) : null}

        <p
          aria-live="polite"
          data-slot="image-frame-output"
          className="text-sm text-muted-foreground"
        >
          {untouched || !size
            ? t("imageFrame.original")
            : t("imageFrame.outputSize", { width: size.width, height: size.height })}
        </p>

        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            disabled={untouched || saving}
            onClick={() => ready && setFrame(defaultState(ready.image, mode))}
          >
            {t("common.reset")}
          </Button>
          <Button type="button" variant="outline" disabled={saving} onClick={onCancel}>
            {t("common.cancel")}
          </Button>
          <Button
            type="button"
            disabled={saving || decoded.status === "decoding"}
            onClick={() => void save()}
          >
            {saving ? <Spinner data-icon="inline-start" /> : null}
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Image-pixel step for one arrow press: 1% of the crop, 10% with Shift. */
function arrowStep(crop: Rect, shift: boolean): number {
  return Math.max(1, Math.max(crop.width, crop.height) * (shift ? 0.1 : 0.01))
}

const ARROWS: Record<string, [number, number]> = {
  ArrowLeft: [-1, 0],
  ArrowRight: [1, 0],
  ArrowUp: [0, -1],
  ArrowDown: [0, 1],
}

type Drag =
  | { kind: "pan"; pointerId: number; x: number; y: number; unit: number; scale: number }
  | {
      kind: "edge"
      edge: Edge
      pointerId: number
      x: number
      y: number
      unit: number
      startCrop: Rect
      layout: StageLayout
    }

/**
 * The square stage: the output rectangle fitted in the middle, exactly what
 * will be uploaded, with the rest of the image dimmed around it. All of it is
 * one SVG in image space, so nothing on it depends on the page's writing
 * direction - an image is not mirrored in Arabic, and neither is its frame.
 */
function FrameStage({
  url,
  image,
  mode,
  frame,
  onFrame,
}: {
  url: string
  image: Size
  mode: FrameMode
  frame: FrameState
  onFrame: React.Dispatch<React.SetStateAction<FrameState | null>>
}) {
  const { t } = useTranslation()
  const svgRef = React.useRef<SVGSVGElement>(null)
  const dragRef = React.useRef<Drag | null>(null)
  // While an edge is dragged the stage keeps the scale it had when the drag
  // began, so the edge stays under the pointer; it refits on release.
  const [frozen, setFrozen] = React.useState<StageLayout | null>(null)
  const clipId = React.useId()
  const hintId = React.useId()

  const live = stageLayout(frame, image, STAGE, PADDING)
  const layout = frozen ? placeFrozen(frozen, frame, image) : live
  const square = isSquareMode(mode)

  const unitOf = () => {
    const width = svgRef.current?.getBoundingClientRect().width ?? 0
    return width > 0 ? width / STAGE : 1
  }

  const onPointerDown = (event: React.PointerEvent<SVGSVGElement>) => {
    if (event.button !== 0) return
    event.currentTarget.setPointerCapture(event.pointerId)
    dragRef.current = {
      kind: "pan",
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      unit: unitOf(),
      scale: live.scale,
    }
  }

  const onEdgePointerDown = (edge: Edge) => (event: React.PointerEvent) => {
    if (event.button !== 0) return
    event.stopPropagation()
    svgRef.current?.setPointerCapture(event.pointerId)
    dragRef.current = {
      kind: "edge",
      edge,
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      unit: unitOf(),
      startCrop: frame.crop,
      layout: live,
    }
    setFrozen(live)
  }

  const onPointerMove = (event: React.PointerEvent<SVGSVGElement>) => {
    const drag = dragRef.current
    if (!drag || drag.pointerId !== event.pointerId) return
    if (drag.kind === "pan") {
      const factor = drag.unit * drag.scale
      const dx = (event.clientX - drag.x) / factor
      const dy = (event.clientY - drag.y) / factor
      drag.x = event.clientX
      drag.y = event.clientY
      // The image follows the pointer, so the crop moves the other way.
      onFrame((current) =>
        current && { ...current, crop: panCrop(current.crop, -dx, -dy, image, mode) }
      )
      return
    }
    const factor = drag.unit * drag.layout.scale
    const horizontal = drag.edge === "left" || drag.edge === "right"
    const delta = horizontal
      ? (event.clientX - drag.x) / factor
      : (event.clientY - drag.y) / factor
    onFrame((current) =>
      current && { ...current, crop: moveEdge(drag.startCrop, drag.edge, delta, image) }
    )
  }

  const endDrag = (event: React.PointerEvent<SVGSVGElement>) => {
    if (dragRef.current?.pointerId !== event.pointerId) return
    dragRef.current = null
    setFrozen(null)
  }

  const onKeyDown = (event: React.KeyboardEvent<SVGSVGElement>) => {
    const arrow = ARROWS[event.key]
    if (!arrow) return
    event.preventDefault()
    const step = arrowStep(frame.crop, event.shiftKey)
    // Like dragging: the image moves the way the arrow points.
    onFrame((current) =>
      current && {
        ...current,
        crop: panCrop(current.crop, -arrow[0] * step, -arrow[1] * step, image, mode),
      }
    )
  }

  const onEdgeKeyDown = (edge: Edge) => (event: React.KeyboardEvent) => {
    const arrow = ARROWS[event.key]
    if (!arrow) return
    const horizontal = edge === "left" || edge === "right"
    const along = horizontal ? arrow[0] : arrow[1]
    // An edge moves along its own axis only; the other arrows still pan.
    if (along === 0) return
    event.preventDefault()
    event.stopPropagation()
    const step = arrowStep(frame.crop, event.shiftKey)
    onFrame((current) =>
      current && { ...current, crop: moveEdge(current.crop, edge, along * step, image) }
    )
  }

  const { output, crop } = layout
  const radius = output.width / 2

  return (
    <div className="flex flex-col gap-2">
      <div className="mx-auto aspect-square w-full max-w-80 overflow-hidden rounded-2xl bg-muted">
        <svg
          ref={svgRef}
          viewBox={`0 0 ${STAGE} ${STAGE}`}
          role="group"
          tabIndex={0}
          aria-label={t("imageFrame.stageLabel")}
          aria-describedby={hintId}
          data-slot="image-frame-stage"
          className="block size-full cursor-grab touch-none outline-none select-none focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:ring-inset active:cursor-grabbing"
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={endDrag}
          onPointerCancel={endDrag}
          onKeyDown={onKeyDown}
        >
          <defs>
            <clipPath id={`${clipId}-crop`}>
              <rect {...crop} />
            </clipPath>
            <clipPath id={`${clipId}-circle`}>
              <circle cx={output.x + radius} cy={output.y + radius} r={radius} />
            </clipPath>
          </defs>
          {/* The rest of the image, dimmed: what the frame leaves out. */}
          <image
            href={url}
            {...layout.image}
            preserveAspectRatio="none"
            className="opacity-30"
          />
          {/* The upload's canvas; what the image does not cover is transparent. */}
          <rect {...output} className="fill-background" />
          <g clipPath={`url(#${clipId}-crop)`}>
            <image
              href={url}
              {...layout.image}
              preserveAspectRatio="none"
              clipPath={mode === "photo" ? `url(#${clipId}-circle)` : undefined}
            />
          </g>
          <rect
            {...output}
            vectorEffect="non-scaling-stroke"
            strokeWidth={1}
            strokeDasharray={frame.margin > 0 ? "4 3" : undefined}
            className="fill-none stroke-foreground/40"
          />
          {mode === "photo" ? (
            <circle
              cx={output.x + radius}
              cy={output.y + radius}
              r={radius}
              vectorEffect="non-scaling-stroke"
              strokeWidth={2}
              className="fill-none stroke-primary"
            />
          ) : (
            <rect
              {...crop}
              vectorEffect="non-scaling-stroke"
              strokeWidth={2}
              className="fill-none stroke-primary"
            />
          )}
          {square
            ? null
            : EDGES.map((edge) => (
                <EdgeHandle
                  key={edge}
                  edge={edge}
                  crop={crop}
                  label={t(`imageFrame.edge.${edge}`)}
                  onPointerDown={onEdgePointerDown(edge)}
                  onKeyDown={onEdgeKeyDown(edge)}
                />
              ))}
        </svg>
      </div>
      <p id={hintId} className="text-center text-xs text-muted-foreground">
        {square ? t("imageFrame.hintMove") : `${t("imageFrame.hintMove")} ${t("imageFrame.hintEdges")}`}
      </p>
    </div>
  )
}

/** Re-places the frame with the scale and image position of a frozen layout. */
function placeFrozen(frozen: StageLayout, frame: FrameState, image: Size): StageLayout {
  const { scale } = frozen
  const originX = frozen.image.x
  const originY = frozen.image.y
  const place = (rect: Rect): Rect => ({
    x: originX + rect.x * scale,
    y: originY + rect.y * scale,
    width: rect.width * scale,
    height: rect.height * scale,
  })
  return {
    scale,
    image: place({ x: 0, y: 0, width: image.width, height: image.height }),
    crop: place(frame.crop),
    output: place(outputRect(frame)),
  }
}

/** Grip length and thickness, in stage units. */
const GRIP_LENGTH = 12
const GRIP_THICKNESS = 2.5
const HIT_THICKNESS = 7

function EdgeHandle({
  edge,
  crop,
  label,
  onPointerDown,
  onKeyDown,
}: {
  edge: Edge
  crop: Rect
  label: string
  onPointerDown: (event: React.PointerEvent) => void
  onKeyDown: (event: React.KeyboardEvent) => void
}) {
  const horizontal = edge === "left" || edge === "right"
  const cx =
    edge === "left" ? crop.x : edge === "right" ? crop.x + crop.width : crop.x + crop.width / 2
  const cy =
    edge === "top" ? crop.y : edge === "bottom" ? crop.y + crop.height : crop.y + crop.height / 2
  const length = Math.min(GRIP_LENGTH, horizontal ? crop.height : crop.width)
  const box = (thickness: number) =>
    horizontal
      ? { x: cx - thickness / 2, y: cy - length / 2, width: thickness, height: length }
      : { x: cx - length / 2, y: cy - thickness / 2, width: length, height: thickness }

  return (
    <g
      tabIndex={0}
      aria-label={label}
      data-edge={edge}
      className={cn(
        "group outline-none",
        horizontal ? "cursor-ew-resize" : "cursor-ns-resize"
      )}
      onPointerDown={onPointerDown}
      onKeyDown={onKeyDown}
    >
      <rect {...box(HIT_THICKNESS)} className="fill-transparent" />
      <rect
        {...box(GRIP_THICKNESS)}
        rx={GRIP_THICKNESS / 2}
        vectorEffect="non-scaling-stroke"
        strokeWidth={2}
        className="fill-primary stroke-background group-focus-visible:stroke-ring"
      />
    </g>
  )
}
