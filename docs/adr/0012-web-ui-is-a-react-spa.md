# The web UI is a React single-page app served by the API

Status: accepted (2026-10-28, #338). Decided by the maintainer on #338.

`Anichron.UI` is one app for the admin screens and the flashback experience: a static
single-page app in React and strict TypeScript, built with Vite, living in `src/Anichron.UI/`
and served by the API container on the same origin as `/api/v1`. It is phone-first and
installable as a PWA; there is no UI server and no server-side rendering.

The options, the criteria and the recommended libraries for every layer are in
`docs/ui/technology-comparison.md`. Those library choices are proposals; each is settled by the
design of the parent issue that owns it, not by this ADR.

## Why React

The criteria, in the maintainer's order: how reliably agents write it, maturity, feel on phones,
testability (including StrykerJS), tooling simplicity, raw performance. React leads the first two
by a clear margin and ties on testability, and it has the strongest accessible, translated
headless components (React Aria Components). Svelte 5 was the runner-up: better on phones and
simpler, but agents still mix its old and new syntax, and its accessible-component ecosystem is
younger. Blazor WebAssembly would have kept one language, but its download and start-up cost on
phones works against Q3 and Q4 of #338.

## Why a static SPA served by the API

Everything is behind a login, private per user and self-hosted, so server rendering buys nothing
(no SEO, no public pages) and would add a Node container. Serving the build output from the API
keeps one origin: no CORS, the refresh cookie and the proxy media cookie stay `SameSite=Strict`,
and the UI and API ship in one image, so they never drift apart.

## Consequences

- Hard to reverse once screens exist: a framework change rewrites every component. The logic kept
  in plain `.ts` modules (API client, auth, player state machine) would survive one.
- The UI brings a second toolchain (Node, pnpm, ESLint, Vitest, Playwright, StrykerJS) with its
  own gates in the `CI gate`, including a 100% mutation threshold on logic modules.
- A UI change rebuilds the API image.
- `<img>` and `<video>` cannot send a bearer header, so proxy routes also accept a cookie
  (its own issue), and proxy URLs must change when their content does (#340).
