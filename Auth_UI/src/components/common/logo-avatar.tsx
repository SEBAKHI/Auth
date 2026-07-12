import { AvatarMenu } from "@/components/common/avatar-menu"
import { EntityAvatar } from "@/components/common/entity-avatar"
import { useLogo } from "@/lib/api/use-logo"

/**
 * Entity logo avatar. When `canEdit`, renders a clickable View/Change/Remove
 * menu that uploads a new logo (same flow as user profile images); otherwise a
 * plain display avatar.
 */
export function LogoAvatar({
  src,
  name,
  canEdit,
  persist,
  invalidate,
  successMessage,
  size = "xl",
}: {
  src?: string | null
  name?: string | null
  canEdit: boolean
  persist: (logoKey: string | null) => Promise<void>
  invalidate: () => void
  successMessage: string
  size?: "default" | "sm" | "lg" | "xl"
}) {
  const logo = useLogo({ persist, invalidate, successMessage })

  if (!canEdit) {
    return <EntityAvatar src={src} name={name} size={size} fit="contain" />
  }

  return (
    <AvatarMenu
      src={src}
      name={name}
      size={size}
      fit="contain"
      onChange={logo.onChange}
      onRemove={logo.onRemove}
      pending={logo.pending}
    />
  )
}
