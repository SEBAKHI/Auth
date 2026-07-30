import { Loader2Icon } from "lucide-react"

import { cn } from "@astoom/ui/utils"

/**
 * Loading indicator. Upstream resolves the glyph through a multi-library
 * placeholder; this project is pinned to lucide, so the icon is imported
 * directly. Carries `role="status"` so assistive tech announces the wait.
 */
function Spinner({ className, ...props }: React.ComponentProps<"svg">) {
  return (
    <Loader2Icon
      data-slot="spinner"
      role="status"
      aria-label="Loading"
      className={cn("size-4 animate-spin", className)}
      {...props}
    />
  )
}

export { Spinner }
