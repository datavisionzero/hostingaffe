import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
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
    // Generated once by the shadcn CLI and owned here (ADR 0017); they export
    // their variants beside the component, which is theirs to do.
    files: ['src/components/ui/**/*.tsx'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
  {
    // A form field with neither `id` nor `name` is one the browser cannot
    // recognise across visits and one no `htmlFor` can point at, and Chrome
    // says so in the console. The two shapes in use here both satisfy it:
    // fields read through `FormData` carry the `name` the request needs
    // anyway, and controlled fields take an `id` from `useId()`.
    //
    // The components under `components/ui/` are excluded: they hand `id` and
    // `name` through with the rest of the props, so the attribute belongs at
    // the call site, where this rule looks for it.
    files: ['src/**/*.tsx'],
    ignores: ['src/components/ui/**/*.tsx'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector:
            'JSXOpeningElement[name.name=/^(input|select|textarea|Input|Textarea)$/]:not(:has(JSXAttribute[name.name=/^(id|name)$/]))',
          message: 'A form field needs an id (from useId) or a name, or the browser cannot tell it apart.',
        },
      ],
    },
  },
])
