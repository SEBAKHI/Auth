/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The IAM site whose https subdomains may frame the account bar (.env.production). */
  readonly VITE_IAM_SITE?: string
}
