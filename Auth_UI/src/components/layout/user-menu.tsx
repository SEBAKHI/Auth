import { useQuery } from "@tanstack/react-query"
import { LogOut, User as UserIcon } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { EntityAvatar } from "@/components/common/entity-avatar"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { useAuth } from "@/lib/auth/auth-context"
import { fullName } from "@/lib/format"

export function UserMenu() {
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
        <Button variant="ghost" className="gap-2 px-2">
          <EntityAvatar
            src={meQuery.data?.profileImageUrl}
            name={name}
            className="size-7"
          />
          <span className="hidden text-sm sm:inline">{name}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel>
          <div className="flex flex-col">
            <span className="text-sm font-medium">{name}</span>
            <span className="truncate text-xs font-normal text-muted-foreground">
              {user?.email}
            </span>
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/profile">
            <UserIcon />
            {t("common.profile")}
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={handleLogout}>
          <LogOut />
          {t("common.signOut")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
