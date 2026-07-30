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
    // House-style guards. Each of these categories was swept to zero, so the rule
    // locks the win in rather than reporting pre-existing debt. Categories still
    // being migrated (space-y-*, raw div+Label form fields, ungrouped Select and
    // DropdownMenu items) are deliberately NOT gated yet — a rule that fires a
    // hundred times is noise nobody reads.
    files: ['**/*.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          // Physical direction breaks the three RTL locales. Logical equivalents:
          // ms-/me-, ps-/pe-, start-/end-, text-start/text-end, border-s/border-e,
          // rounded-s*/rounded-e*.
          selector:
            'JSXAttribute[name.name="className"] Literal[value=/(^|\\s)(ml|mr|pl|pr|left|right|border-l|border-r|rounded-l|rounded-r)-|(^|\\s)text-(left|right)(\\s|$)/]',
          message:
            'Use logical properties (ms/me, ps/pe, start/end, text-start/text-end, border-s/border-e, rounded-s/rounded-e) — the app ships RTL locales.',
        },
        {
          // The preset owns colour. Raw scales bypass it and break dark mode.
          selector:
            'JSXAttribute[name.name="className"] Literal[value=/(^|\\s)(text|bg|border|ring|fill|stroke|from|via|to)-(red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|slate|gray|zinc|neutral|stone)-[0-9]/]',
          message:
            'Use semantic tokens (bg-primary, text-muted-foreground, text-destructive, var(--chart-N)) — never a raw Tailwind colour scale.',
        },
        {
          // The native picker renders browser chrome the preset cannot reach.
          selector: 'JSXAttribute[name.name="type"] > Literal[value="date"]',
          message:
            'Use DatePicker from @astoom/ui/common/date-picker instead of a native date input.',
        },
      ],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: 'lucide-react',
              importNames: ['Loader2', 'Loader2Icon'],
              message:
                'Use Spinner from @astoom/ui/spinner — it owns the spin animation and the status role.',
            },
          ],
        },
      ],
    },
  },
  {
    // `dialog.tsx` and `alert-dialog.tsx` centre physically on purpose; `dialog.tsx`
    // documents at length why logical centring plus an `rtl:` translate caused a
    // feedback loop that overflowed the viewport, and the alert dialog cites that
    // same note. `sonner.tsx` maps the toast loading glyph, which is what Spinner
    // itself wraps.
    files: [
      'packages/ui/src/dialog.tsx',
      'packages/ui/src/alert-dialog.tsx',
      'packages/ui/src/sonner.tsx',
      'packages/ui/src/spinner.tsx',
    ],
    rules: {
      'no-restricted-syntax': 'off',
      'no-restricted-imports': 'off',
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
