import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['**/dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // The design system is not an HMR boundary: its primitives deliberately
    // export their `cva` variant builders (`buttonVariants`, `toggleVariants`, …)
    // next to the component, which is the upstream shadcn shape and what lets
    // sibling components compose them. `only-export-components` is aimed at app
    // route/page modules, where a stale non-component export really does break
    // fast refresh.
    files: ['packages/ui/src/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
])
