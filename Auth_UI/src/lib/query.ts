import { QueryClient } from "@tanstack/react-query"

/**
 * Shared React Query client.
 *
 * `retry: 1` lets a transient 401 succeed on the second attempt after the auth
 * middleware has silently refreshed the access token.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 0,
    },
  },
})
