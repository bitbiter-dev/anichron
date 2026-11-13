import js from '@eslint/js';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import tseslint from 'typescript-eslint';

const textAttributes = [
  'title',
  'placeholder',
  'aria-label',
  'aria-description',
  'aria-placeholder',
  'aria-roledescription',
  'aria-valuetext',
];
const attributePattern = (names) => `/^(${names.join('|')})$/`;
const networkGlobals = ['fetch', 'EventSource', 'XMLHttpRequest', 'WebSocket'];
const onlyApi = (name) => ({
  name,
  message: `Only src/api/ talks to the network; call it instead of ${name}.`,
});
const catalogText = [
  `JSXAttribute[name.name=${attributePattern(textAttributes)}] > Literal`,
  `JSXAttribute[name.name="alt"] > Literal[value!=""]`,
  `JSXAttribute[name.name=${attributePattern(['alt', ...textAttributes])}] > JSXExpressionContainer > :matches(Literal, TemplateLiteral)`,
].map((selector) => ({
  selector,
  message: 'A user-visible attribute takes its text from the catalog through t(...).',
}));
const moduleScope =
  ':matches(Program, ExportNamedDeclaration) > VariableDeclaration > VariableDeclarator';
const importTime = [
  `${moduleScope} > :function`,
  `${moduleScope} > :matches(ObjectExpression, ArrayExpression, TemplateLiteral, Literal[raw=/^['"]/], Literal[value=true], Literal[value=false])`,
  `${moduleScope} > :matches(CallExpression, NewExpression) :matches(ObjectExpression, ArrayExpression, TemplateLiteral, Literal[raw=/^['"]/], :function)`,
].map((selector) => ({
  selector,
  message:
    'Module scope runs at import, so its mutants are static and run every test (docs/mutation-testing.md). Use a function declaration, or build the value in a function.',
}));

export default defineConfig([
  globalIgnores([
    'dist',
    'coverage',
    'reports',
    '.stryker-tmp',
    'playwright-report',
    'test-results',
    'src/api/generated',
  ]),
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
      '@typescript-eslint/only-throw-error': [
        'error',
        {
          allow: [
            { from: 'package', package: '@tanstack/router-core', name: 'Redirect' },
            { from: 'package', package: '@tanstack/router-core', name: 'NotFoundError' },
          ],
          allowRethrowing: true,
        },
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
      'no-restricted-syntax': ['error', ...catalogText],
    },
  },
  {
    files: ['src/**/*.ts', 'src/app/router.tsx'],
    ignores: [
      'src/**/*.test.ts',
      'src/**/*.d.ts',
      'src/test/**',
      'src/api/generated/**',
      'src/shared/ui/tv.ts',
    ],
    rules: {
      'no-restricted-syntax': ['error', ...catalogText, ...importTime],
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
    files: ['src/design/**'],
    rules: {
      'react/jsx-no-literals': 'off',
      'no-restricted-syntax': 'off',
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
