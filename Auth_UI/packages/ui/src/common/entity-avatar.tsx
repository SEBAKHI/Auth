import { Avatar, AvatarFallback, AvatarImage } from "@authsystem/ui/avatar"
import { initials } from "@authsystem/ui/format"
import { cn } from "@authsystem/ui/utils"

/**
 * Circular entity avatar: shows the image when present, otherwise the name's
 * initials. Used for user profile images and organization/application logos.
 * Photos fill the circle (`cover`); logos keep their aspect ratio inside it,
 * so logo surfaces pass `fit="contain"`.
 *
 * `bare` draws the image with no circle and no ring around it: application
 * icons, which carry their own shape (owner, 2026-11-02). Initials, when there
 * is no image, keep their circle.
 */
export function EntityAvatar({
  src,
  name,
  size = "default",
  fit = "cover",
  bare = false,
  className,
}: {
  src?: string | null
  name?: string | null
  size?: "default" | "sm" | "lg" | "xl"
  fit?: "cover" | "contain"
  bare?: boolean
  className?: string
}) {
  return (
    <Avatar
      size={size}
      className={cn(bare && "rounded-none after:hidden", className)}
    >
      {src ? (
        <AvatarImage
          src={src}
          alt={name ?? ""}
          className={cn(fit === "contain" && "object-contain", bare && "rounded-none")}
        />
      ) : null}
      <AvatarFallback>{initials(name)}</AvatarFallback>
    </Avatar>
  )
}
