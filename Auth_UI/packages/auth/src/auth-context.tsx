/* eslint-disable react-refresh/only-export-components */
import { useQueryClient } from "@tanstack/react-query"
import * as React from "react"

import { api, SESSION_EXPIRED_EVENT } from "@authsystem/api/client"
import { claimToArray, decodeJwt } from "@authsystem/api/jwt"
import { resetUserScopedCache } from "@authsystem/api/query"
import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setTokens,
} from "@authsystem/api/token-store"
import i18n, {
  applyLanguage,
  persistLanguage,
  SUPPORTED_LANGUAGES,
  type LanguageCode,
} from "@authsystem/i18n"
import { setActiveTimeZone } from "@authsystem/i18n/timezone"
import { setDataTableScope } from "@authsystem/ui/data-table/storage"
import type { UserInfo } from "@authsystem/api/types"

import {
  clearPendingTwoFactorChallenge,
  setPendingTwoFactorChallenge,
} from "./pending-challenge"
import { permissionMatches } from "./permission-matching"

type AuthStatus = "loading" | "authenticated" | "unauthenticated"

/**
 * Provider-specific sign-in extras: Apple sends the one-time authorization
 * code (exchanged server-side for the revocable refresh token) and, on the
 * FIRST authorization only, the user's name.
 */
export interface ExternalLoginExtras {
  authorizationCode?: string
  givenName?: string
  familyName?: string
}

export type LoginResult =
  | { status: "authenticated"; requiresPasswordChange: boolean }
  | { status: "twoFactorRequired"; challengeToken: string }

interface AuthContextValue {
  status: AuthStatus
  user: UserInfo | null
  roles: string[]
  permissions: string[]
  hasPermission: (permission: string | undefined) => boolean
  hasAnyPermission: (permissions: string[]) => boolean
  login: (email: string, password: string) => Promise<LoginResult>
  loginExternal: (
    provider: string,
    idToken: string,
    nonce?: string,
    extras?: ExternalLoginExtras
  ) => Promise<LoginResult>
  recoverAccount: (
    email: string,
    password: string,
    twoFactorCode?: string
  ) => Promise<LoginResult>
  recoverAccountExternal: (
    provider: string,
    idToken: string,
    nonce?: string,
    twoFactorCode?: string
  ) => Promise<LoginResult>
  completeTwoFactor: (
    challengeToken: string,
    code: string,
    useRecoveryCode: boolean
  ) => Promise<{ requiresPasswordChange: boolean }>
  completeEmailVerification: (email: string, otp: string) => Promise<LoginResult>
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
  // Read from context rather than importing the singleton: the provider is
  // mounted inside QueryClientProvider in both apps, and a test that renders
  // AuthProvider with its own client must be able to observe the reset.
  const queryClient = useQueryClient()

  const { roles, permissions } = React.useMemo(() => derive(user), [user])

  // Adopt the profile's preferred language once per session, so a login on a
  // fresh browser follows the profile without fighting a mid-session toggle.
  const languageAdoptedRef = React.useRef(false)
  const applyProfilePreferences = React.useCallback(
    (profile: UserInfo | null | undefined) => {
      setActiveTimeZone(profile?.timeZone)
      // Binds stored table layouts to this account and pulls the server copy.
      // Without it, two accounts on one browser share one set of layouts.
      setDataTableScope(profile?.id ?? null)

      if (languageAdoptedRef.current) return
      languageAdoptedRef.current = true
      const code = profile?.preferredLanguage
      const supported = SUPPORTED_LANGUAGES.some((l) => l.code === code)
      if (code && supported && i18n.language !== code) {
        persistLanguage(code as LanguageCode)
        void applyLanguage(code as LanguageCode)
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
      // The session is gone but the tab is not: whatever the previous account
      // left in the cache would otherwise be waiting for whoever signs in next.
      void resetUserScopedCache(queryClient)
      return
    }
    setUser(data)
    setStatus("authenticated")
    applyProfilePreferences(data)
  }, [applyProfilePreferences, queryClient])

  // Bootstrap an existing session on first load (silent refresh via middleware).
  React.useEffect(() => {
    if (!getRefreshToken()) return
    // Cross an async boundary before the request so bootstrap cannot create a
    // cascading render from the effect that installed it.
    const timer = window.setTimeout(() => void loadCurrentUser(), 0)
    return () => window.clearTimeout(timer)
  }, [loadCurrentUser])

  // React to a non-recoverable session loss raised by the API client.
  React.useEffect(() => {
    const handler = () => {
      setUser(null)
      setStatus("unauthenticated")
      setActiveTimeZone(null)
      setDataTableScope(null)
      // The most dangerous of the four resets. An expiry ends the session
      // without anyone choosing to leave, so the app is mid-screen with its
      // queries populated, and the next sign-in on this tab is the likeliest of
      // all to be a different person. Note that scoping a key by user id could
      // not have covered this case: there is no id to scope by here.
      void resetUserScopedCache(queryClient)
    }
    window.addEventListener(SESSION_EXPIRED_EVENT, handler)
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler)
  }, [queryClient])

  // Shared tail of every login variant: either a 2FA challenge (no tokens
  // yet, the verify step completes the session) or a full token response.
  const adoptLoginResponse = React.useCallback(
    (data: {
      token?: { accessToken: string; refreshToken: string } | null
      user?: UserInfo | null
      requiresPasswordChange?: boolean
      twoFactorChallengeToken?: string | null
    }): LoginResult => {
      if (data.twoFactorChallengeToken) {
        setPendingTwoFactorChallenge(data.twoFactorChallengeToken)
        return {
          status: "twoFactorRequired",
          challengeToken: data.twoFactorChallengeToken,
        }
      }
      if (!data.token || !data.user) {
        throw new Error("Login failed")
      }

      // The challenge is settled, so its mirrored destination has served its
      // purpose too. Leaving it set would let a later sign-in in the same tab
      // pick up a stale pending request and resume someone else's flow.
      clearPendingTwoFactorChallenge()
      // Before the tokens, not after: from this line on every query in the
      // cache would be read as belonging to the incoming account. The other
      // three resets should have left nothing behind, which is exactly why this
      // one is worth keeping — it is the only reset that runs on the path where
      // a mistake becomes someone else's data.
      void resetUserScopedCache(queryClient)
      setTokens(data.token.accessToken, data.token.refreshToken)
      setUser(data.user)
      setStatus("authenticated")
      applyProfilePreferences(data.user)

      return {
        status: "authenticated",
        requiresPasswordChange: data.requiresPasswordChange ?? false,
      }
    },
    [applyProfilePreferences, queryClient]
  )

  const login = React.useCallback(
    async (email: string, password: string): Promise<LoginResult> => {
      // The browser identifier is no longer passed here. It rides on every
      // request as a header set by the API client, so the three flows that used
      // to remember it and the two that forgot are now identical.
      const { data, error } = await api.POST("/api/v1/Auth/login", {
        body: { email, password },
      })
      if (error || !data) {
        throw error ?? new Error("Login failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  const loginExternal = React.useCallback(
    async (
      provider: string,
      idToken: string,
      nonce?: string,
      extras?: ExternalLoginExtras
    ): Promise<LoginResult> => {
      const { data, error } = await api.POST("/api/v1/Auth/external-login", {
        body: {
          provider,
          idToken,
          nonce,
          authorizationCode: extras?.authorizationCode,
          givenName: extras?.givenName,
          familyName: extras?.familyName,
        },
      })
      if (error || !data) {
        throw error ?? new Error("External login failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  // Grace-period recovery: cancels a pending account deletion, restores the
  // account and signs the user in — the response is a full login body, so it
  // rides the same shared tail.
  const recoverAccount = React.useCallback(
    async (
      email: string,
      password: string,
      twoFactorCode?: string
    ): Promise<LoginResult> => {
      const { data, error } = await api.POST("/api/v1/Auth/deletion/recover", {
        body: { email, password, twoFactorCode },
      })
      if (error || !data) {
        throw error ?? new Error("Account recovery failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  const recoverAccountExternal = React.useCallback(
    async (
      provider: string,
      idToken: string,
      nonce?: string,
      twoFactorCode?: string
    ): Promise<LoginResult> => {
      const { data, error } = await api.POST(
        "/api/v1/Auth/deletion/recover-external",
        {
          body: { provider, idToken, nonce, twoFactorCode },
        }
      )
      if (error || !data) {
        throw error ?? new Error("Account recovery failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  const completeTwoFactor = React.useCallback(
    async (
      challengeToken: string,
      code: string,
      useRecoveryCode: boolean
    ): Promise<{ requiresPasswordChange: boolean }> => {
      const { data, error } = await api.POST("/api/v1/auth/2fa/verify", {
        body: { challengeToken, code, useRecoveryCode },
      })
      if (error || !data) {
        throw error ?? new Error("Two-factor verification failed")
      }

      const result = adoptLoginResponse(data)
      if (result.status !== "authenticated") {
        throw new Error("Two-factor verification failed")
      }
      return { requiresPasswordChange: result.requiresPasswordChange }
    },
    [adoptLoginResponse]
  )

  const completeEmailVerification = React.useCallback(
    async (email: string, otp: string): Promise<LoginResult> => {
      // The anonymous verify-email path confirms the address and signs the user
      // in, returning the same body as login. Feed it through the shared tail so
      // a 2FA challenge (defensive) or a full session is handled identically.
      const { data, error } = await api.POST("/api/v1/Auth/verify-email", {
        body: { email, otp },
      })
      if (error || !data) {
        throw error ?? new Error("Email verification failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
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
    // Stops further persistence; the stored layouts stay put so signing back
    // in restores them. Only the *next* account is prevented from inheriting.
    setDataTableScope(null)
    languageAdoptedRef.current = false
    // Awaited here, unlike the other three call sites: this is the one path
    // with somewhere to await from, and the logout request above may well have
    // raced a page's refetch that would otherwise land after the removal.
    await resetUserScopedCache(queryClient)
  }, [queryClient])

  const hasPermission = React.useCallback(
    (permission: string | undefined) => {
      if (!permission) return true
      return permissionMatches(permissions, permission)
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
      loginExternal,
      recoverAccount,
      recoverAccountExternal,
      completeTwoFactor,
      completeEmailVerification,
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
      loginExternal,
      recoverAccount,
      recoverAccountExternal,
      completeTwoFactor,
      completeEmailVerification,
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
