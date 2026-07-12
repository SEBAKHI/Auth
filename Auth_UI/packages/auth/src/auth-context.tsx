/* eslint-disable react-refresh/only-export-components */
import * as React from "react"

import { api, SESSION_EXPIRED_EVENT } from "@astoom/api/client"
import { claimToArray, decodeJwt } from "@astoom/api/jwt"
import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setTokens,
} from "@astoom/api/token-store"
import i18n, {
  persistLanguage,
  SUPPORTED_LANGUAGES,
  type LanguageCode,
} from "@astoom/i18n"
import { setActiveTimeZone } from "@astoom/i18n/timezone"
import type { UserInfo } from "@astoom/api/types"

type AuthStatus = "loading" | "authenticated" | "unauthenticated"

export interface LoginResult {
  requiresPasswordChange: boolean
  requiresTwoFactor: boolean
}

interface AuthContextValue {
  status: AuthStatus
  user: UserInfo | null
  roles: string[]
  permissions: string[]
  hasPermission: (permission: string | undefined) => boolean
  hasAnyPermission: (permissions: string[]) => boolean
  login: (email: string, password: string) => Promise<LoginResult>
  logout: () => Promise<void>
  refreshUser: () => Promise<void>
}

const AuthContext = React.createContext<AuthContextValue | undefined>(undefined)

/** Permissions/roles come from the access-token claims, falling back to /me. */
function derive(user: UserInfo | null): {
  roles: string[]
  permissions: string[]
} {
  const token = getAccessToken()
  const claims = token ? decodeJwt(token) : null

  const permissions =
    user?.permissions && user.permissions.length > 0
      ? user.permissions
      : claimToArray(claims?.permissions)
  const roles =
    user?.roles && user.roles.length > 0
      ? user.roles
      : claimToArray(claims?.roles)

  return { roles, permissions }
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = React.useState<AuthStatus>(() =>
    getRefreshToken() ? "loading" : "unauthenticated"
  )
  const [user, setUser] = React.useState<UserInfo | null>(null)

  const { roles, permissions } = React.useMemo(() => derive(user), [user])

  // Adopt the profile's preferred language once per session, so a login on a
  // fresh browser follows the profile without fighting a mid-session toggle.
  const languageAdoptedRef = React.useRef(false)
  const applyProfilePreferences = React.useCallback(
    (profile: UserInfo | null | undefined) => {
      setActiveTimeZone(profile?.timeZone)

      if (languageAdoptedRef.current) return
      languageAdoptedRef.current = true
      const code = profile?.preferredLanguage
      const supported = SUPPORTED_LANGUAGES.some((l) => l.code === code)
      if (code && supported && i18n.language !== code) {
        persistLanguage(code as LanguageCode)
        void i18n.changeLanguage(code)
      }
    },
    []
  )

  const loadCurrentUser = React.useCallback(async () => {
    const { data, error } = await api.GET("/api/v1/Auth/me")
    if (error || !data) {
      clearTokens()
      setUser(null)
      setStatus("unauthenticated")
      return
    }
    setUser(data)
    setStatus("authenticated")
    applyProfilePreferences(data)
  }, [applyProfilePreferences])

  // Bootstrap an existing session on first load (silent refresh via middleware).
  React.useEffect(() => {
    if (getRefreshToken()) {
      void loadCurrentUser()
    }
  }, [loadCurrentUser])

  // React to a non-recoverable session loss raised by the API client.
  React.useEffect(() => {
    const handler = () => {
      setUser(null)
      setStatus("unauthenticated")
      setActiveTimeZone(null)
    }
    window.addEventListener(SESSION_EXPIRED_EVENT, handler)
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler)
  }, [])

  const login = React.useCallback(
    async (email: string, password: string): Promise<LoginResult> => {
      const { data, error } = await api.POST("/api/v1/Auth/login", {
        body: { email, password },
      })
      if (error || !data) {
        throw error ?? new Error("Login failed")
      }

      setTokens(data.token.accessToken, data.token.refreshToken)
      setUser(data.user)
      setStatus("authenticated")
      applyProfilePreferences(data.user)

      return {
        requiresPasswordChange: data.requiresPasswordChange ?? false,
        requiresTwoFactor: data.requiresTwoFactor ?? false,
      }
    },
    [applyProfilePreferences]
  )

  const logout = React.useCallback(async () => {
    try {
      await api.POST("/api/v1/Auth/logout", {
        body: { logoutAllDevices: false },
      })
    } catch {
      /* best-effort; clear local state regardless */
    }
    clearTokens()
    setUser(null)
    setStatus("unauthenticated")
    setActiveTimeZone(null)
    languageAdoptedRef.current = false
  }, [])

  const hasPermission = React.useCallback(
    (permission: string | undefined) => {
      if (!permission) return true
      return permissions.includes("*") || permissions.includes(permission)
    },
    [permissions]
  )

  const hasAnyPermission = React.useCallback(
    (required: string[]) => {
      if (required.length === 0) return true
      return required.some((p) => hasPermission(p))
    },
    [hasPermission]
  )

  const value = React.useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      roles,
      permissions,
      hasPermission,
      hasAnyPermission,
      login,
      logout,
      refreshUser: loadCurrentUser,
    }),
    [
      status,
      user,
      roles,
      permissions,
      hasPermission,
      hasAnyPermission,
      login,
      logout,
      loadCurrentUser,
    ]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = React.useContext(AuthContext)
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider")
  }
  return context
}
