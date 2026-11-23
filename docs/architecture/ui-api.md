# UI API module

How the UI (`src/Anichron.UI`) calls the API.

## Where calls live

- `src/api/client.ts`: `createApiClient(tabs)` composes the router context's `api`, the session plus one module per area. Only `src/api/` touches the network (ESLint).
- `src/api/session.ts`: every call that issues, renews or ends a session (`restore`, `login`, `logout`, `changePassword`), the timer, visibility and tab handling, and the signed-in state.
- `src/api/<area>.ts`: data calls, one module per area, created by its first caller. A data call is never a session method.

## Data calls

- An area module sends each call through `authorized`, which `createSession` hands to `client.ts`: the bearer token, and on a `401` one refresh and one retry. A token renewed meanwhile is retried without refreshing; a second `401` signs out.
- A refresh that fails without signing out throws `SessionNotRenewed`, and the query retry predicate (`src/app/query-client.ts`) stops on it.
- An area module returning memories maps each asset through the `ShowMemories` that `client.ts` hands it (`memories.ts`): it resolves proxy URLs, relative to `/api/v1/`, and picks the compact preview.
- A lapsed media session is renewed once, then the failure stands (`media.renewOnce` in `src/api/media.ts`). An `<img>` or `<video>` cannot see a `401`, so its error renews and loads the source once more. One showing an object URL fails at once. See "The refresh rules".
- `src/api/media.ts` decodes a thumbnail with `new Image()` (`loadImage`), which reads the HTTP cache (proxy URLs are immutable). It settles even when refused; the `<img>` recovers as above.
- `media.downloads` is the app's one download store: one download at a time, the first unfinished in the order a story wants (`wantedOf` in `features/on-this-day/prefetch.ts`), each into an object URL; an earlier one stops a later one. A `401` renews the media session and retries once, by the same rule. What arrived stays for the session, within the limits in `createDownloads`.
- A story opens once its first picture has arrived (`loadStory` in `features/on-this-day/day.ts`) and renders only pictures from the store. A picture fails only once none of it has arrived for `loadMilliseconds` (`mediaProgressed` in `story.ts`). A Live Photo video the server refuses plays from the network instead.

## The event stream

- `src/api/event-stream.ts` reads a Server-Sent Events response with `fetch`, so the request carries the bearer token through `authorized`. An area module imports it lazily (`work-items.ts`).
- It reconnects from the last event id after a clean end, a network failure or a refusal, waiting `Retry-After` or its backoff (`createBackoff`, `src/shared/backoff.ts`, shared with the session and the start screen's retry). A `403` or `404` ends it for good.
- The caller owns the `AbortSignal`: the work queue closes its stream on unmount and while the tab is hidden (`features/work-items/use-stream.ts`).

## The signed-in user

- The session holds the `user` from every session response and clears it with the session.
- A change of user, signing out included, in this tab or from another, clears the query cache (`createAppRouter`), and `SignedIn` remounts its views per user, so the next user never sees the previous one's data.
- `session.user()` is synchronous and stable between changes. Views read it through `useUser()` (`src/shared/use-user.ts`).
- Signed in means a user is held. The `signed-in` route's `beforeLoad` puts it in the route context, so a guard below it reads `context.user`; the login route calls `isSignedIn()`.
- A user without photos (`hasPhotos` in `src/features/on-this-day/day.ts`) gets no flashback request: Today shows the no-photos message in place of the memories, and the day route redirects to `/`.
- A user in a forced password change (`mustChangePassword`) reaches no shell route: the `shell` route's `beforeLoad` redirects to `/change-password`, carrying the address in `redirect`. Once the flag is clear, that route redirects to `returnPath`. The API enforces the rule on its own.

## Failures

- A call the server refuses or cannot answer throws `ProblemError`, carrying the UI's `Problem` (`src/api/problem.ts`).
- `problemOf(error)` reads it, and anything else becomes the generic problem. A screen keys its messages by `ErrorCode`.
- A form is `useProblemForm` (`src/shared/use-problem-form.ts`): a problem naming a sent field shows on that field, any other on the form, and `Retry-After` counts down (`FormStatus`).
- A screen whose data fails to load shows `shared/ui/Failed.tsx` with its own "failed" message, or the shared "unreachable" one (`failureMessage`, `src/shared/failure.ts`).

## The refresh rules

The single-flight refresh, its schedule, the `401` handling and the tab messages are in `session.ts` and written once. CLAUDE.md, "Session" and "Tabs", states them.

- A media error refreshes only when the media cookie may have lapsed (`renewMedia()`). A lapsed cookie is the only `401` a proxy gives a signed-in user (ADR 0013), and refresh is rate-limited.
- A device clock fast enough to lose the cookie before `renewAt` keeps its media broken until the scheduled refresh.
