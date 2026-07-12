import type { components, paths } from "./schema"

/** All API DTOs, keyed by their backend schema name. */
export type Schemas = components["schemas"]

export type { paths }

// Frequently used aliases for convenience across the app.
export type UserInfo = Schemas["UserInfo"]
export type TokenResponse = Schemas["TokenResponse"]
export type LoginResponse = Schemas["LoginResponse"]
export type ProblemDetails = Schemas["ProblemDetails"]
