import { clsx, type ClassValue } from "clsx"
import { extendTailwindMerge } from "tailwind-merge"

/*
 * The height names preset.css adds to the spacing scale (`h-control`,
 * `size-control-sm`, `py-cell-y`…). Without them tailwind-merge cannot tell
 * that `h-control` and `h-8` set the same property, keeps both, and the
 * stylesheet's order, not the caller, decides which height a control gets.
 */
const twMerge = extendTailwindMerge({
  extend: {
    theme: {
      spacing: ["control", "control-sm", "control-lg", "cell-y"],
    },
  },
})

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
