# The web UI is a React single-page app served by the API

Status: accepted (decided on #338)

## Decision

`Anichron.UI` is one app for the admin screens and the flashbacks:

- a static single-page app in React and strict TypeScript, built with Vite;
- served by the API container on the same origin as `/api/v1`;
- phone-first and installable as a PWA, with no UI server and no server-side rendering.

Each layer's library is settled by its own parent issue (#344–#349). The full comparison is archived on #338.

## Why

- **React:** the maintainer's criteria, in order, were: how reliably agents write it, maturity, feel on phones, testability, tooling simplicity, raw performance. React leads on the first two and has the strongest accessible headless components.
- **Served by the API:** everything sits behind a login, so server rendering buys nothing. One origin means no CORS, `SameSite=Strict` cookies, and a UI and API that ship together.

## Rejected

- **Svelte 5:** agents still mix its old and new syntax, and its accessible components are younger.
- **Blazor WebAssembly:** its download and start-up cost on phones.

## Consequences

- Hard to reverse once screens exist. Logic kept in plain `.ts` would survive a framework change.
- A second toolchain (Node, pnpm, ESLint, Vitest, Playwright, StrykerJS), with its own gates.
- A UI change rebuilds the API image.
- `<img>` and `<video>` cannot send a bearer header, so proxies accept a cookie (ADR 0013), and proxy URLs change when their content does.
