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

The service worker needs no entry: its routes (`src/app/app-shell.ts`) and its entry (`src/app/sw.ts`) are mutated like any `.ts`.

### Individual sites (`// Stryker disable`)

A directive needs a row naming its file and the ADR 0005 proof: no input tells the mutant apart, and no rewrite removes the branch.

| File | Site | Proof |
| --- | --- | --- |
| `src/features/on-this-day/day.ts` | `storySearch` (`BlockStatement`, `ObjectLiteral`) | The router keeps the root's parsed search, so the story reads `year` whatever the validator returns, and `startGroup` opens any unknown year at the first group. TanStack types a route's search only through `validateSearch`, so every form of it keeps the same equivalent mutants. |
| `src/features/on-this-day/day.ts` | `recentMemoryOf`'s filter (`ObjectLiteral`) | Without it the search also reads every other query's data, but only a `Day` or a recent day carries a `date`. A `Day` for the date sends `loadStory` down the day branch, so the search never meets one. `loadStory` cannot name the tray's own queries, whose windows need the signed-in user's today. |
| `src/features/on-this-day/day.ts` | `pictureDecoded`'s `url ?? ''` (`StringLiteral`) | A memory without a thumbnail has no decoded picture. Any fallback key reads no query, because `pictureQuery` keys are only the URLs the landing asked for, and those are never empty or Stryker's text. |
| `src/features/on-this-day/story.ts` | `at`'s `++visits` (`UpdateOperator`) | A visit number is only a key, React's and the video player's, that must differ from the last one; counting down gives every visit a distinct number as counting up does, and any counter keeps the same mutant. |
| `src/features/removed/use-drag-select.ts` | `follow`'s `index === current.last` (`ConditionalExpression`) | It skips painting the row already painted; painting it again sets the same selection, so nothing a user sees differs. |
| `src/features/removed/use-drag-select.ts` | the `touchmove` listener's `{ passive: false }` (`ObjectLiteral`) | jsdom treats an omitted `passive` as false, so the empty options behave the same there; only a browser defaults a window `touchmove` to passive. `passive: true` is killed. |
| `src/features/removed/use-drag-select.ts` | the unmount effect's `[]` (`ArrayDeclaration`) | A constant dependency list runs the effect once, as the empty one does. |
| `src/features/on-this-day/use-story.ts` | `subscribe`'s `stopDownloads()` (`CallExpression`) | Once the story is gone its listener does nothing: the story's clock starts only while the story has a listener, and the player syncs only while its video is mounted. A listener left behind only holds memory, and every form of the subscription ends in the same call. |
| `src/features/on-this-day/use-today.ts` | the new day's `to: '.'` (`StringLiteral`) | TanStack Router reads an empty `to` as `'.'` (`router-core`'s `buildLocation`), so the mutant navigates to the same place. The navigation must keep the current screen, which only `'.'` names, and `to` is required by its type. |

## Consequences

- A view that needs a branch, computed value or timer gets a hook in `.ts`.
- An equivalent mutant is rewritten away before it is suppressed.
- The setup is guarded like the backend's: thresholds 100, the Vitest runner, no `ignoreStatic`, the static-leaks reporter loaded, `--force` only in `scripts/ui-mutation-changed.sh`.
- Pull requests run incrementally against master's baseline, which every master push publishes: a tested push promotes its PR's file. A mutant keeps its result while its code and covering tests are unchanged, and a setup changed since the baseline's commit runs every mutant. What that cannot see, the weekly full run catches (`docs/mutation-testing.md`, "The UI").
- `src/shared/ui/tv.ts` may build at module scope: views call `tv(...)` at import, so its mutants are static however it is written.
