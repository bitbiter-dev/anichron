import { relative } from 'node:path';

interface PlannedMutant {
  fileName: string;
  location: { start: { line: number } };
  static?: boolean;
  coveredBy?: readonly string[];
}

interface PlanReady {
  mutantPlans: readonly { mutant: PlannedMutant }[];
}

export function staticLeaks({ mutantPlans }: PlanReady, root: string) {
  const places = mutantPlans
    .map(({ mutant }) => mutant)
    .filter((mutant) => mutant.static === true && (mutant.coveredBy ?? []).length > 0)
    .map(
      (mutant) => `${relative(root, mutant.fileName)}:${String(mutant.location.start.line + 1)}`,
    );
  return [...new Set(places)].sort((a, b) => a.localeCompare(b, 'en', { numeric: true }));
}

class StaticLeaksReporter {
  static inject = [] as const;

  onMutationTestingPlanReady(event: PlanReady) {
    const leaks = staticLeaks(event, process.cwd());
    if (leaks.length === 0) return;
    console.error(
      [
        'code ran outside a test (a request, timer or callback outliving it), so these static mutants would rerun the whole suite:',
        ...leaks.map((leak) => `  ${leak}`),
      ].join('\n'),
    );
    process.exit(1);
  }
}

export const strykerPlugins = [
  { kind: 'Reporter', name: 'static-leaks', injectableClass: StaticLeaksReporter },
];
