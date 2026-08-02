import { Avatar, AvatarFallback, AvatarImage } from "@authsystem/ui/avatar"
import { initials } from "@authsystem/ui/format"

/**
 * Circular entity avatar: shows the image when present, otherwise the name's
 * initials. Used for user profile images and organization/application logos.
 * Photos fill the circle (`cover`); logos keep their aspect ratio inside it,
 * so logo surfaces pass `fit="contain"`.
 */
export function EntityAvatar({
  src,
  name,
  size = "default",
  fit = "cover",
  className,
}: {
  src?: string | null
  name?: string | null
  size?: "default" | "sm" | "lg" | "xl"
  fit?: "cover" | "contain"
  className?: string
}) {
  return (
    <Avatar size={size} className={className}>
      {src ? (
        <AvatarImage
          src={src}
          alt={name ?? ""}
          className={fit === "contain" ? "object-contain" : undefined}
        />
      ) : null}
      <AvatarFallback>{initials(name)}</AvatarFallback>
    </Avatar>
  )
}
