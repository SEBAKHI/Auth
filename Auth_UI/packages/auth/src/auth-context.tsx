/* eslint-disable react-refresh/only-export-components */
import * as React from "react"

import { api, SESSION_EXPIRED_EVENT } from "@authsystem/api/client"
import { claimToArray, decodeJwt } from "@authsystem/api/jwt"
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

/**
 * In-memory fallback for the pending 2FA challenge so a lost navigation state
 * (e.g. a re-render race) doesn't strand the verify page. Never persisted —
 * a page refresh intentionally sends the user back to /login.
 */
let pendingTwoFactorChallenge: string | null = null

export function getPendingTwoFactorChallenge(): string | null {
  return pendingTwoFactorChallenge
}

/**
 * Drops the pending challenge when the user abandons the 2FA step to sign in as
 * someone else. Without this the verify page would still find a challenge for
 * the previous account and send them straight back to it.
 */
export function clearPendingTwoFactorChallenge(): void {
  pendingTwoFactorChallenge = null
}

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
      setDataTableScope(null)
    }
    window.addEventListener(SESSION_EXPIRED_EVENT, handler)
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler)
  }, [])

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
        pendingTwoFactorChallenge = data.twoFactorChallengeToken
        return {
          status: "twoFactorRequired",
          challengeToken: data.twoFactorChallengeToken,
        }
      }
      if (!data.token || !data.user) {
        throw new Error("Login failed")
      }

      pendingTwoFactorChallenge = null
      setTokens(data.token.accessToken, data.token.refreshToken)
      setUser(data.user)
      setStatus("authenticated")
      applyProfilePreferences(data.user)

      return {
        status: "authenticated",
        requiresPasswordChange: data.requiresPasswordChange ?? false,
      }
    },
    [applyProfilePreferences]
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
