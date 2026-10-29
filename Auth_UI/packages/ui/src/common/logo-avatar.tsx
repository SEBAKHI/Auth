import { AvatarMenu } from "@authsystem/ui/common/avatar-menu"
import { EntityAvatar } from "@authsystem/ui/common/entity-avatar"
import { useLogo } from "@authsystem/api/use-logo"

/**
 * Entity logo avatar. When `canEdit`, renders a clickable View/Change/Remove
 * menu that uploads a new logo (same flow as user profile images); otherwise a
 * plain display avatar.
 */
export function LogoAvatar({
  src,
  name,
  label,
  canEdit,
  persist,
  invalidate,
  successMessage,
  size = "xl",
  frame = "logo",
}: {
  src?: string | null
  name?: string | null
  /** The visible label beside the circle, as its accessible name (`AvatarMenu`). */
  label?: string
  canEdit: boolean
  persist: (logoKey: string | null) => Promise<void>
  invalidate: () => void
  successMessage: string
  size?: "default" | "sm" | "lg" | "xl"
  /** A favicon is framed square; every other logo keeps a free aspect. */
  frame?: "logo" | "favicon"
}) {
  const logo = useLogo({ persist, invalidate, successMessage })

  if (!canEdit) {
    return <EntityAvatar src={src} name={name} size={size} fit="contain" />
  }

  return (
    <AvatarMenu
      src={src}
      name={name}
      label={label}
      size={size}
      fit="contain"
      frame={frame}
      onChange={logo.onChange}
      onRemove={logo.onRemove}
      pending={logo.pending}
    />
  )
}
