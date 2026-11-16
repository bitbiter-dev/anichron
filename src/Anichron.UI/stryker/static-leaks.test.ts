import assert from 'node:assert/strict';
import { test } from 'node:test';
import { staticLeaks } from './static-leaks.ts';

function plan(fileName: string, line: number, isStatic: boolean, coveredBy: string[]) {
  return { mutant: { fileName, location: { start: { line } }, static: isStatic, coveredBy } };
}

void test('lists a static mutant that tests cover, once per line, with one-based lines', () => {
  const leaks = staticLeaks(
    {
      mutantPlans: [
        plan('/ui/src/b.ts', 9, true, ['t']),
        plan('/ui/src/b.ts', 9, true, ['t']),
        plan('/ui/src/a.ts', 1, true, ['t']),
      ],
    },
    '/ui',
  );

  assert.deepEqual(leaks, ['src/a.ts:2', 'src/b.ts:10']);
});

void test('passes module scope and runtime coverage', () => {
  const leaks = staticLeaks(
    { mutantPlans: [plan('/ui/src/tv.ts', 3, true, []), plan('/ui/src/a.ts', 3, false, ['t'])] },
    '/ui',
  );

  assert.deepEqual(leaks, []);
});
