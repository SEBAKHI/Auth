import { useQuery } from "@tanstack/react-query"
import { LogOut, User as UserIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { EntityAvatar } from "@astoom/ui/common/entity-avatar"
import { Button } from "@astoom/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@astoom/ui/dropdown-menu"
import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { fullName } from "@astoom/ui/format"

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
        <Button variant="ghost" size="icon" aria-label={name}>
          <EntityAvatar
            src={meQuery.data?.profileImageUrl}
            name={name}
            className="size-7"
          />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel>
          <div className="flex items-center gap-2">
            <EntityAvatar
              src={meQuery.data?.profileImageUrl}
              name={name}
              className="size-8"
            />
            <div className="flex min-w-0 flex-col">
              <span className="truncate text-sm font-medium">{name}</span>
              <span className="truncate text-xs font-normal text-muted-foreground">
                {user?.email}
              </span>
            </div>
          </div>
        </DropdownMenuLabel>
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
