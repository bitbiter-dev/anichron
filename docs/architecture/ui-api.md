# UI API module

How the UI (`src/Anichron.UI`) calls the API.

## Where calls live

- `src/api/client.ts`: `createApiClient(tabs)` composes the router context's `api`, the session plus one module per area. Only `src/api/` touches the network (ESLint).
- `src/api/session.ts`: every call that issues, renews or ends a session (`restore`, `login`, `logout`), the timer, visibility and tab handling, and the signed-in state.
- `src/api/<area>.ts`: data calls, one module per area, created by its first caller. Adding one never changes the session's interface.

## The signed-in user

- The session holds the `user` from every session response and clears it with the session. Nothing reads the signed-in user through `/users/me`.
- `session.user()` is synchronous and stable between changes: route guards read it in `beforeLoad`, views through `useUser()` (`src/shared/use-user.ts`).
- Signed in means a user is held (`isSignedIn()`).

## Failures

- A call resolves its data or throws `ProblemError`, carrying the UI's `Problem` (`src/api/problem.ts`): code, field errors, retry delay.
- `problemOf(error)` reads it, and anything else becomes the generic problem. A screen keys its messages by `ErrorCode`.

## The refresh rules

The single-flight refresh, its schedule and backoff, the `401` handling and the tab messages are in `session.ts` and written once. CLAUDE.md, "Session" and "Tabs", states them.
