# UI mutation scope: logic in `.ts`, views out

Status: accepted

## Decision

StrykerJS mutates the UI's logic (`.ts` under `src/Anichron.UI/src/`) at 100%. Views (`.tsx`) are out, except the route tree. Anything in scope that survives is a missing test.

## Why

- A view's mutants are mostly markup. Killing them pins structure, while the UI's tests assert what the user sees.
- Branching is kept out of views instead:
  - Logic, hooks included, lives in `.ts`.
  - dependency-cruiser forbids `.ts` importing `.tsx` (`logic-imports-no-views`).
  - ESLint caps `.tsx` at `complexity` 4 and `max-depth` 2.
- Views are still tested through seam 2. That just doesn't count toward the score.

## Scope (`mutate` in `src/Anichron.UI/stryker.config.json`)

Starts from `src/**/*.ts`. Every other entry is listed here, and `scripts/ui-mutation-guard.sh` fails on one that is not.

| Entry | Why |
| --- | --- |
| `src/app/router.tsx` | Brought back in: the route tree's paths and parent links are logic, and seam 2 kills their mutants |
| `!src/**/*.test.ts` | Tests |
| `!src/**/*.d.ts` | No runtime code |
| `!src/test/**` | Test support (ADR 0005, rule 4) |
| `!src/i18n/locales/**` | Catalogs are data |
| `!src/api/generated/**` | Generated (ADR 0005, rule 1) |

### Individual sites (`// Stryker disable`)

A directive needs a row naming its file and the ADR 0005 proof: no input tells the mutant apart, and no rewrite removes the branch.

| File | Site | Proof |
| --- | --- | --- |
| `src/features/on-this-day/day.ts` | `storySearch` (`ArrowFunction`, `ObjectLiteral`) | The router keeps the root's parsed search, so the story reads `year` whatever the validator returns, and `startGroup` opens any unknown year at the first group. TanStack types a route's search only through `validateSearch`, so every form of it keeps the same equivalent mutants. |
| `src/app/router.tsx` | the admin guard's `user()?.isAdmin` (`OptionalChaining`) | The guard runs under `signed-in`, whose `beforeLoad` redirects whenever no user is held, so `user()` is never undefined there. Its type still allows it, so any form of the read keeps the same mutant. |
| `src/features/on-this-day/story.ts` | `at`'s `++visits` (`UpdateOperator`) | A visit number is only a React key that must differ from the last one; counting down gives every visit a distinct number as counting up does, and any counter keeps the same mutant. |

## Consequences

- A view that needs a branch, computed value or timer gets a hook in `.ts`.
- An equivalent mutant is rewritten away before it is suppressed.
- The setup is guarded like the backend's: thresholds 100, the Vitest runner, no incremental mode, `--mutate` only in `scripts/ui-mutation-changed.sh`.
