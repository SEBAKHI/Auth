import { cn } from "@authsystem/ui/utils"

/**
 * Renders a permission code so it reads the same in every language.
 *
 * A code is a Latin identifier with neutral characters in it — colons, dots and
 * a trailing asterisk. Inside an Arabic, Urdu or Persian page the paragraph
 * direction is right-to-left, and the bidirectional algorithm resolves those
 * trailing neutrals to the paragraph direction rather than to the run they
 * belong to. So `org:members:*` was painted as `*:org:members`, and
 * `notification-templates:*` as `*:notification-templates` — the same characters
 * in a different order, which reads as a different code.
 *
 * `<bdi dir="ltr">` isolates the run: it stops the surrounding direction from
 * reaching inside, and stops the code from disturbing the sentence around it.
 * The same treatment the privacy-notice version strings already get.
 */
export function PermissionCode({
  code,
  className,
}: {
  code: string
  className?: string
}) {
  return (
    <bdi dir="ltr" className={cn("inline-block", className)}>
      {code}
    </bdi>
  )
}
