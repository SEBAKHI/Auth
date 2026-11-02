import { useQuery } from "@tanstack/react-query"
import { LogOut, User as UserIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import {
  USER_MENU_CONTENT_CLASS,
  UserAvatarButton,
  UserMenuIdentity,
} from "@authsystem/ui/common/user-identity"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useAuth } from "@authsystem/auth/auth-context"
import { fullName } from "@authsystem/ui/format"

/**
 * The avatar at the end of the header and its menu. The person's applications
 * are not in it: the grid button beside it opens them (`AppsLauncher`).
 */
export function UserMenu({
  profileHref = "/profile",
  showProfile = true,
}: {
  /** In-app route the profile entry points to. */
  profileHref?: string
  /** Hide the profile entry where the shell nav already links to it. */
  showProfile?: boolean
} = {}) {
  const { t } = useTranslation()
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  // Shares the ["me"] cache with the Profile page, so a changed avatar updates here too.
  const meQuery = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(api.GET("/api/v1/Users/me")),
  })

  const name = fullName(user?.firstName, user?.lastName, user?.email ?? "")

  const handleLogout = async () => {
    await logout()
    toast.success(t("auth.signedOut"))
    navigate("/login", { replace: true })
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <UserAvatarButton name={name} src={meQuery.data?.profileImageUrl} />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className={USER_MENU_CONTENT_CLASS}>
        <UserMenuIdentity
          name={name}
          email={user?.email}
          src={meQuery.data?.profileImageUrl}
        />
        <DropdownMenuSeparator />
        {showProfile ? (
          <>
            <DropdownMenuGroup>
              <DropdownMenuItem asChild>
                <Link to={profileHref}>
                  <UserIcon />
                  {t("common.profile")}
                </Link>
              </DropdownMenuItem>
            </DropdownMenuGroup>
            <DropdownMenuSeparator />
          </>
        ) : null}
        <DropdownMenuGroup>
          <DropdownMenuItem onClick={handleLogout}>
            <LogOut />
            {t("common.signOut")}
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
