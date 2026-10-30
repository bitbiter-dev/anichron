import js from '@eslint/js';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import tseslint from 'typescript-eslint';

const visibleAttributes =
  '/^(alt|title|placeholder|aria-label|aria-description|aria-placeholder|aria-roledescription|aria-valuetext)$/';
const networkGlobals = ['fetch', 'EventSource', 'XMLHttpRequest', 'WebSocket'];
const onlyApi = (name) => ({
  name,
  message: `Only src/api/ talks to the network; call it instead of ${name}.`,
});

export default defineConfig([
  globalIgnores(['dist', 'coverage', 'reports', '.stryker-tmp']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, tseslint.configs.strictTypeChecked],
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      '@typescript-eslint/no-empty-object-type': [
        'error',
        { allowInterfaces: 'with-single-extends' },
      ],
    },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    extends: [reactHooks.configs.flat.recommended, jsxA11y.flatConfigs.strict],
    plugins: { react },
    languageOptions: { globals: globals.browser },
    settings: { react: { version: 'detect' } },
    rules: {
      'react/jsx-no-literals': ['error', { noStrings: true, ignoreProps: true }],
      'no-restricted-syntax': [
        'error',
        {
          selector: `JSXAttribute[name.name=${visibleAttributes}] :matches(Literal, TemplateLiteral)`,
          message: 'A user-visible attribute takes its text from the catalog through t(...).',
        },
      ],
    },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/api/**'],
    rules: {
      'no-restricted-globals': ['error', ...networkGlobals.map(onlyApi)],
      'no-restricted-properties': [
        'error',
        ...['window', 'globalThis', 'self'].flatMap((object) =>
          networkGlobals.map((name) => ({
            object,
            property: name,
            message: onlyApi(name).message,
          })),
        ),
      ],
    },
  },
  {
    files: ['src/**/*.tsx'],
    rules: {
      complexity: ['error', 4],
      'max-depth': ['error', 2],
    },
  },
]);
