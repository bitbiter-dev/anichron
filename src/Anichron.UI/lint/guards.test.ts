import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { ESLint } from 'eslint';
import tseslint from 'typescript-eslint';

const guardRules = new Set([
  'no-restricted-syntax',
  'no-restricted-globals',
  'no-restricted-properties',
  'no-restricted-imports',
]);

const eslint = new ESLint({
  overrideConfig: [
    tseslint.configs.disableTypeChecked,
    { languageOptions: { parserOptions: { projectService: false } } },
  ],
  ruleFilter: ({ ruleId }) => guardRules.has(ruleId),
});

async function messages(filePath: string, code: string) {
  const [result] = await eslint.lintText(code, { filePath });
  return (result?.messages ?? []).map(({ message }) => message);
}

const snippets = {
  visibilityEvent: [
    "export function f(g: () => void) { document.addEventListener('visibilitychange', g); }",
    'listens for visibility',
  ],
  visibilityState: ['export function f() { return document.visibilityState; }', 'reads visibility'],
  hidden: ['export function f() { return document.hidden; }', 'reads visibility'],
  eventTarget: ['export function f() { return new EventTarget(); }', 'createChanges()'],
  listenerSet: ['export function f() { return new Set<() => void>(); }', 'createChanges()'],
  focusRing: [
    "export function f() { return 'outline-none focus-visible:outline-focus'; }",
    'focus-ring utility',
  ],
  focusRingTemplate: [
    'export function f(x: string) { return `${x} outline-focus`; }',
    'focus-ring utility',
  ],
  focusVariant: [
    "export function f() { return { isFocusVisible: { true: 'a' } }; }",
    'focusVariant()',
  ],
  backdrop: ["export function f() { return 'grid glow-backdrop'; }", 'CentredPage'],
  glass: [
    "export function f() { return 'rounded-card glass-card'; }",
    'Card (src/shared/ui/Card.tsx)',
  ],
  useForm: [
    "import { useForm } from 'react-hook-form';\nexport const f = useForm;",
    'useProblemForm',
  ],
  userEvent: [
    "import { userEvent } from '@testing-library/user-event';\nuserEvent.setup();",
    'user()',
  ],
  timers: [
    'export async function f() { await vi.advanceTimersByTimeAsync(1); }',
    'src/test/time.ts',
  ],
  runAll: ['export async function f() { await vi.runAllTimersAsync(); }', 'src/test/time.ts'],
  importTime: ['export const f = { a: 1 };', 'Module scope runs at import'],
  sharedWorkers: ['export function f() { vi.resetModules(); }', 'share a worker'],
  networkGlobal: [
    "export function f() { return fetch('/a'); }",
    'Only src/api/ talks to the network',
  ],
  networkProperty: [
    "export function f() { return window.fetch('/a'); }",
    'Only src/api/ talks to the network',
  ],
  catalogText: ['export const F = () => <p title="Hi" />;', 'from the catalog'],
} as const;

type Snippet = keyof typeof snippets;

async function fires(filePath: string, snippet: Snippet) {
  const [code, message] = snippets[snippet];
  return (await messages(filePath, code)).some((text) => text.includes(message));
}

function check(filePath: string, guarded: readonly Snippet[], allowed: readonly Snippet[]) {
  void describe(filePath, () => {
    for (const snippet of guarded)
      void test(`forbids ${snippet}`, async () => {
        assert.equal(await fires(filePath, snippet), true);
      });
    for (const snippet of allowed)
      void test(`allows ${snippet}`, async () => {
        assert.equal(await fires(filePath, snippet), false);
      });
  });
}

const everywhere: readonly Snippet[] = [
  'visibilityState',
  'hidden',
  'eventTarget',
  'listenerSet',
  'focusRing',
  'focusRingTemplate',
  'focusVariant',
  'backdrop',
  'glass',
  'useForm',
  'userEvent',
  'timers',
  'runAll',
  'sharedWorkers',
];

check(
  'src/features/x/x.ts',
  [...everywhere, 'visibilityEvent', 'importTime', 'networkGlobal', 'networkProperty'],
  [],
);
check(
  'src/features/x/X.tsx',
  ['visibilityEvent', 'catalogText', 'glass', 'networkGlobal'],
  ['importTime'],
);
check(
  'src/features/x/x.test.ts',
  ['visibilityEvent', 'timers', 'userEvent', 'networkGlobal'],
  ['importTime'],
);
check(
  'src/api/x.ts',
  [...everywhere, 'visibilityEvent', 'importTime'],
  ['networkGlobal', 'networkProperty'],
);
check(
  'src/shared/listen.ts',
  ['importTime', 'sharedWorkers', 'networkGlobal', 'networkProperty', 'focusRing', 'useForm'],
  ['visibilityEvent', 'visibilityState', 'hidden', 'eventTarget', 'listenerSet'],
);
check(
  'src/shared/listen.test.ts',
  ['visibilityEvent', 'visibilityState', 'sharedWorkers', 'networkProperty', 'timers'],
  ['eventTarget', 'listenerSet'],
);
check(
  'src/shared/ui/tv.ts',
  ['focusRing', 'eventTarget', 'sharedWorkers', 'networkGlobal'],
  ['focusVariant', 'importTime'],
);
check('src/shared/ui/tv.test.ts', ['focusRing', 'timers'], ['focusVariant']);
check('src/shared/ui/CentredPage.tsx', ['catalogText', 'glass', 'networkGlobal'], ['backdrop']);
for (const file of ['src/shared/ui/Card.tsx', 'src/app/TopBar.tsx', 'src/app/BottomBar.tsx'])
  check(file, ['catalogText', 'backdrop', 'focusRing', 'networkGlobal'], ['glass']);
check(
  'src/shared/use-problem-form.ts',
  ['importTime', 'userEvent', 'sharedWorkers', 'networkProperty', 'visibilityEvent'],
  ['useForm'],
);
check(
  'src/test/x.ts',
  [
    'visibilityState',
    'eventTarget',
    'useForm',
    'sharedWorkers',
    'networkGlobal',
    'networkProperty',
  ],
  ['visibilityEvent', 'timers', 'runAll', 'userEvent', 'importTime'],
);
check('src/design/X.tsx', ['networkGlobal'], ['catalogText', 'glass', 'backdrop', 'focusRing']);
