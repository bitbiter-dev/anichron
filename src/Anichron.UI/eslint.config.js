import js from '@eslint/js';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import tseslint from 'typescript-eslint';
import stryker from './stryker.config.json' with { type: 'json' };

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
const sharedWorkers = {
  object: 'vi',
  property: 'resetModules',
  message:
    'Test files share a worker (isolate: false), so a reset registry reaches every later file. Import a fresh copy with a query instead.',
};
const catalogText = [
  `JSXAttribute[name.name=${attributePattern(textAttributes)}] > Literal`,
  `JSXAttribute[name.name="alt"] > Literal[value!=""]`,
  `JSXAttribute[name.name=${attributePattern(['alt', ...textAttributes])}] > JSXExpressionContainer > :matches(Literal, TemplateLiteral)`,
].map((selector) => ({
  selector,
  message: 'A user-visible attribute takes its text from the catalog through t(...).',
}));
const mutated = stryker.mutate.filter((glob) => !glob.startsWith('!'));
const notMutated = stryker.mutate
  .filter((glob) => glob.startsWith('!'))
  .map((glob) => glob.slice(1));
const moduleScope =
  ':matches(Program, ExportNamedDeclaration) > VariableDeclaration > VariableDeclarator';
const typed = ':matches(TSAsExpression, TSSatisfiesExpression)';
const importTime = [
  moduleScope,
  `${moduleScope} > ${typed}`,
  `${moduleScope} > ${typed} > ${typed}`,
]
  .flatMap((parent) => [
    `${parent} > :function`,
    `${parent} > :matches(ObjectExpression, ArrayExpression, TemplateLiteral, Literal[raw=/^['"]/], Literal[value=true], Literal[value=false])`,
    `${parent} > :matches(CallExpression, NewExpression) :matches(ObjectExpression, ArrayExpression, TemplateLiteral, Literal[raw=/^['"]/], :function)`,
  ])
  .map((selector) => ({
    selector,
    message:
      'Module scope runs at import, so its mutants are static and run every test (docs/mutation-testing.md). Use a function declaration, or build the value in a function.',
  }));

const classes = (pattern) =>
  [`Literal[value=${pattern}]`, `TemplateElement[value.raw=${pattern}]`].join(', ');
const only = (message) => (selector) => ({ selector, message });
const syntaxGuards = {
  visibility: ['Literal[value="visibilitychange"]'].map(
    only('Only src/shared/listen.ts listens for visibility; use onVisible or onVisibility.'),
  ),
  notifiers: [
    'NewExpression[callee.name="EventTarget"]',
    'NewExpression[callee.name="Set"][typeArguments.params.0.type="TSFunctionType"]',
  ].map(only('A change notifier is createChanges() from src/shared/listen.ts.')),
  focusRing: [classes('/\\boutline-focus\\b/')].map(
    only('The focus ring is the focus-ring utility (src/app/focus.css).'),
  ),
  focusVariant: ['Property[key.name="isFocusVisible"]'].map(
    only('A tv focus state is focusVariant from src/shared/ui/tv.ts.'),
  ),
  backdrop: [classes('/\\bglow-backdrop\\b/')].map(
    only('A standalone screen is CentredPage (src/shared/ui/CentredPage.tsx).'),
  ),
  glass: [classes('/\\bglass-card\\b/')].map(
    only('A glass panel is Card (src/shared/ui/Card.tsx).'),
  ),
};

const visibilityProperties = ['visibilityState', 'hidden'].map((property) => ({
  object: 'document',
  property,
  message: 'Only src/shared/listen.ts reads visibility; use isVisible.',
}));
const timerProperties = [
  'advanceTimersByTime',
  'advanceTimersByTimeAsync',
  'runAllTimers',
  'runAllTimersAsync',
].map((property) => ({
  object: 'vi',
  property,
  message: 'Advance the clock through src/test/time.ts, which runs it inside act().',
}));
const networkProperties = ['window', 'globalThis', 'self'].flatMap((object) =>
  networkGlobals.map((name) => ({ object, property: name, message: onlyApi(name).message })),
);
const propertyGuards = { visibility: visibilityProperties, timers: timerProperties };

const importGuards = {
  useForm: [
    {
      name: 'react-hook-form',
      importNames: ['useForm'],
      message: 'A form is useProblemForm (src/shared/use-problem-form.ts).',
    },
  ],
  userEvent: [
    {
      name: '@testing-library/user-event',
      message: 'Type and click through user() from src/test/time.ts, which advances fake timers.',
    },
  ],
};

const guarded = (guards, exempt) =>
  Object.entries(guards)
    .filter(([name]) => !exempt.includes(name))
    .flatMap(([, entries]) => entries);

function syntax({ mutated = false, exempt = [] } = {}) {
  return [
    'error',
    ...catalogText,
    ...(mutated ? importTime : []),
    ...guarded(syntaxGuards, exempt),
  ];
}

function properties({ api = false, exempt = [] } = {}) {
  return [
    'error',
    sharedWorkers,
    ...(api ? [] : networkProperties),
    ...guarded(propertyGuards, exempt),
  ];
}

function imports({ exempt = [] } = {}) {
  return ['error', { paths: guarded(importGuards, exempt) }];
}

const source = ['src/**/*.{ts,tsx}'];
const api = ['src/api/**/*.{ts,tsx}'];
const listenModule = ['src/shared/listen.ts'];
const testSupport = ['src/test/**'];

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
    files: source,
    extends: [reactHooks.configs.flat.recommended, jsxA11y.flatConfigs.strict],
    plugins: { react },
    languageOptions: { globals: globals.browser },
    settings: { react: { version: 'detect' } },
    rules: {
      'react/jsx-no-literals': ['error', { noStrings: true, ignoreProps: true }],
      'no-restricted-syntax': syntax(),
      'no-restricted-globals': ['error', ...networkGlobals.map(onlyApi)],
      'no-restricted-properties': properties(),
      'no-restricted-imports': imports(),
    },
  },
  {
    files: mutated,
    ignores: [...notMutated, 'src/shared/ui/tv.ts'],
    rules: { 'no-restricted-syntax': syntax({ mutated: true }) },
  },
  {
    files: api,
    rules: {
      'no-restricted-globals': 'off',
      'no-restricted-properties': properties({ api: true }),
    },
  },
  {
    files: listenModule,
    rules: {
      'no-restricted-syntax': syntax({ mutated: true, exempt: ['visibility', 'notifiers'] }),
      'no-restricted-properties': properties({ exempt: ['visibility'] }),
    },
  },
  {
    files: ['src/shared/listen.test.ts'],
    rules: { 'no-restricted-syntax': syntax({ exempt: ['notifiers'] }) },
  },
  {
    files: ['src/shared/ui/tv.ts', 'src/shared/ui/tv.test.ts'],
    rules: { 'no-restricted-syntax': syntax({ exempt: ['focusVariant'] }) },
  },
  {
    files: ['src/shared/ui/CentredPage.tsx'],
    rules: { 'no-restricted-syntax': syntax({ exempt: ['backdrop'] }) },
  },
  {
    files: ['src/shared/ui/Card.tsx', 'src/app/TopBar.tsx', 'src/app/BottomBar.tsx'],
    rules: { 'no-restricted-syntax': syntax({ exempt: ['glass'] }) },
  },
  {
    files: ['src/shared/use-problem-form.ts'],
    rules: { 'no-restricted-imports': imports({ exempt: ['useForm'] }) },
  },
  {
    files: testSupport,
    rules: {
      'no-restricted-syntax': syntax({ exempt: ['visibility'] }),
      'no-restricted-properties': properties({ exempt: ['timers'] }),
      'no-restricted-imports': imports({ exempt: ['userEvent'] }),
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
