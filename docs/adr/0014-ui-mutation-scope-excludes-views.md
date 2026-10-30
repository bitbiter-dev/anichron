# UI mutation scope: logic in `.ts`, views out

Status: accepted (2026-10-30, #358). The split was decided by the maintainer on #344 (Q4); this
ADR records it, as [ADR 0005](0005-mutation-scope-excludes-unkillable-and-wiring-code.md) does for
the backend.

StrykerJS mutates the UI's **logic**, the `.ts` files under `src/Anichron.UI/src/`, at the same
100% threshold as the backend. **Views**, the `.tsx` files, are out of scope except for the route
tree. Everything in scope that survives is a missing test.

## Why views are excluded

A view's mutants are mostly in its markup: a removed element, an emptied attribute, a swapped
class name. Killing them takes a test per element, which pins the component's structure, and the
UI's tests assert what the user sees instead (#344, "Testing Decisions"). The branching in a view
is what carries real behaviour, and the decision keeps it out of views rather than mutating them:

- Logic, including custom hooks without JSX, lives in `.ts`. dependency-cruiser's
  `logic-imports-no-views` rule forbids a `.ts` file importing a `.tsx` file, so logic cannot lean
  on a view.
- ESLint caps `complexity` at 4 and `max-depth` at 2 in `.tsx` files, so a view that grows real
  branching fails lint and has to move it into a hook.
- Views are still tested: the whole app is rendered through its real route tree, with MSW at the
  network boundary and `vitest-axe` on the result (seam 2). That catches a broken screen. It does
  not count towards the score.

## What is excluded, and what is brought back

`mutate` in `src/Anichron.UI/stryker.config.json` starts from `src/**/*.ts`. Every other entry is
listed here, and `scripts/ui-mutation-guard.sh` fails on one that is not.

| Entry | Why |
| --- | --- |
| `src/app/router.tsx` | Brought back into scope. The route tree is code-based (Q5) and references the page components, so it is a `.tsx` file under the `.ts` → `.tsx` rule. Its paths and parent links are logic, and seam 2 kills their mutants. Decided by the maintainer on #358. |
| `!src/**/*.test.ts` | Tests. |
| `!src/**/*.d.ts` | Declaration files have no runtime code. |
| `!src/test/**` | Test support (MSW server, the render helper, setup), referenced only by tests; ADR 0005, rule 4. |
| `!src/i18n/locales/**` | The translation catalogs are data. A wrong string fails the seam-2 tests that read it. |
| `!src/api/generated/**` | API types generated from the OpenAPI document (#347): generated code, ADR 0005, rule 1. |

### Individual sites (`// Stryker disable`)

None yet. A directive needs a row here naming its file, and the same proof as ADR 0005: no input
can tell the original and the mutant apart, and no rewrite removes the branch.

## Consequences

- A view that needs a branch, a computed value or a timer gets a hook in a `.ts` file, tested
  through seam 2 or directly (seam 3) when seam 2 cannot reasonably kill a mutant.
- An equivalent mutant in a `.ts` file is rewritten away before it is suppressed. The first one,
  `initAsync: false` in `createI18n`, was: with inline catalogs i18next initialises synchronously
  either way (measured), so the option was dropped.
- The setup is guarded like the backend's: thresholds 100, the Vitest runner, no incremental
  mode, `--mutate` only in `scripts/ui-mutation-changed.sh`, and workflows run StrykerJS only
  through that script.
