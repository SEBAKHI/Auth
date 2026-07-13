import { AvatarMenu } from "@astoom/ui/common/avatar-menu"
import { EntityAvatar } from "@astoom/ui/common/entity-avatar"
import { useLogo } from "@astoom/api/use-logo"

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
  trim = false,
}: {
  src?: string | null
  name?: string | null
  canEdit: boolean
  persist: (logoKey: string | null) => Promise<void>
  invalidate: () => void
  successMessage: string
  size?: "default" | "sm" | "lg" | "xl"
  /**
   * Trim padded margins on upload. Off by default: this avatar is a circle,
   * so uploads keep their own margins exactly like user photos; only enable
   * where the stored image also renders at natural aspect ratio elsewhere
   * (platform wordmark/favicon).
   */
  trim?: boolean
}) {
  const logo = useLogo({ persist, invalidate, successMessage, trim })

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
