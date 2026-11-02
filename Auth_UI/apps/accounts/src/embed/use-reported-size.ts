import * as React from "react"

import { tellHost } from "./protocol"

/**
 * Tells the loader the size of the element the returned ref is put on, now and whenever it
 * changes, so the frame is exactly as large as what it draws: the bar, or a panel with its
 * shadow. Whole pixels, rounded up, so nothing is cut by a fraction.
 *
 * `around`: room the frame keeps on every side of the element (a panel's shadow), added to
 * the size. `inset`: how far inside the frame's edge what it draws begins (that room, or
 * the element's own padding), so the loader lines up what is drawn, not the frame.
 *
 * A callback ref, not an object: a panel's content is mounted by Radix after this
 * component's effects have run, and an effect reading `ref.current` then would find nothing
 * and never measure.
 */
export function useReportedSize<T extends HTMLElement = HTMLDivElement>(
  parentOrigin: string,
  around = 0,
  inset = around
): React.RefCallback<T> {
  const [element, setElement] = React.useState<T | null>(null)
  React.useLayoutEffect(() => {
    if (!element) return
    let last = ""
    const report = () => {
      // The layout size, not the drawn one: a panel opens with a zoom (a transform), which
      // the bounding box would report as a smaller panel.
      const width = element.offsetWidth + 2 * around
      const height = element.offsetHeight + 2 * around
      const key = `${width}x${height}`
      if (key === last) return
      last = key
      tellHost(parentOrigin, { type: "size", width, height, inset })
    }
    report()
    const observer = new ResizeObserver(report)
    observer.observe(element)
    return () => observer.disconnect()
  }, [element, parentOrigin, around, inset])
  return setElement
}
