import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { initials } from "@/lib/format"

/**
 * Circular entity avatar: shows the image when present, otherwise the name's
 * initials. Used for user profile images and organization/application logos.
 */
export function EntityAvatar({
  src,
  name,
  size = "default",
  className,
}: {
  src?: string | null
  name?: string | null
  size?: "default" | "sm" | "lg" | "xl"
  className?: string
}) {
  return (
    <Avatar size={size} className={className}>
      {src ? <AvatarImage src={src} alt={name ?? ""} /> : null}
      <AvatarFallback>{initials(name)}</AvatarFallback>
    </Avatar>
  )
}
