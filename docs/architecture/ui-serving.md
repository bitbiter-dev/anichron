# UI serving

How the API serves `Anichron.UI` on the same origin as `/api/v1` (ADR 0012). Code: `Anichron.API/Infrastructure/UiServing.cs`, `SecurityHeaders.cs`.

## The image

- The API Dockerfile builds the UI in a Node stage and copies `dist/` to `/app/wwwroot`.
- **Node and pnpm pins move together:**
  - Node: the `FROM` tag, `.nvmrc`, `engines` and `devEngines.runtime.version`.
  - pnpm: `packageManager` and the Dockerfile's `pnpm@`.
  - Dependabot rewrites only the `FROM` line, so its PR gets the rest added before it merges.
- Without a built UI there is no `wwwroot`: the API works, and every UI path is a `404`. Develop through `pnpm dev`.

## Serving rules

| Request | Response |
|---|---|
| A file under `/assets/` | the file, `immutable` for a year |
| Any other file in the web root | the file, `no-cache` |
| `GET`/`HEAD` of a path without an extension, outside `/api` | `index.html`, `no-cache` |
| An unknown path under `/api` | `404` Problem Details |
| An unknown path with an extension, or another method | `404`, empty body |
| A known `/api` route with the wrong method | `405` |

- The fallbacks are middleware, not `MapFallback`: a catch-all endpoint would answer every method, so a known route could no longer return `405`.
- `/api` is matched as a path segment (`/apiary` is an app path). A dot in the last segment counts as an extension, so client routes never end in one.
- The web root is read through ASP.NET Core's file provider (ADR 0007).

## The Home Screen files

- `src/Anichron.UI/public/` holds `manifest.webmanifest`, the icons it names, `apple-touch-icon.png` and their source `icon.svg`. Vite copies them unhashed to the web root, so they are served `no-cache`.
- The PNGs are exported from the SVG once and committed through Git LFS, not built. A new icon means exporting all four again: Playwright's Chromium at device scale 1, the SVG in an `<img>` of N × N px, a screenshot of that clip.
- One icon serves iOS and Android. It is opaque and full bleed, with the sun and its reflection inside the centred 80% circle a maskable crop keeps.
- `index.html` links the manifest and the touch icon, with one `theme-color` per colour scheme (the two surface colours). The manifest takes the dark surface.

## Service worker

- `src/app/sw.ts` builds to `/sw.js` (vite-plugin-pwa in `injectManifest` mode, `vite.config.ts`). It sits in the web root, so it is served `no-cache`, and it is not part of the initial JS.
- `src/app/register-service-worker.ts` registers it only in a production build, through a Trusted Types policy. `pnpm dev` has none.
- **Precached:** the files the plugin's `globPatterns` name. The main build emits `theme-init.js`, so the chosen theme survives an offline start.
- **Navigations** to an app page are answered from the precached `index.html`, by the server's fallback rule (`src/app/app-shell.ts`). A change to that rule changes both.
- **Never routed:** every request under `/api`, navigation or not, and every file not precached. They go to the network, and the service worker caches none of them.
- **No media cache.** Media relies on the browser's HTTP cache, where a current proxy URL is `immutable` (`media-reads.md`). The UI requests only the proxy URLs the API listed for the signed-in user, so the cache never shows one user's media to another, and cache limits and videos are not the service worker's concern.
- With no network, the precached frame starts and the start-up refresh fails as unreachable (`StartFailed`).
- **Updates:** `register-service-worker.ts` checks for a new version at start and when the page becomes visible, no more often than `checkMilliseconds`. It offers a version that installed while an older one controls the page, and returns the port the router context carries as `updates`.
- A waiting version never activates by itself. The shell's `UpdateNotice` shows above the bottom bar, except while a story is open. Reload posts the take-over message (`take-over.ts`) and reloads once the new version controls the page.

## Security headers

- `UseSecurityHeaders` runs first and sets the headers in `OnStarting`, so every response gets them, errors included.
- Outside `/api`: a strict CSP (`script-src 'self'`, `style-src 'self'`, Trusted Types required), `Referrer-Policy`, `nosniff` and a deny-all `Permissions-Policy`. Under `/api`: `nosniff` and `frame-ancestors 'none'`. The exact values are in `SecurityHeaders.cs`.
- There is no `Development` relaxation.
- React Aria injects an inline `<style>` unless an element with id `react-aria-pressable-style` exists. `index.html` carries an empty `<template>` with that id, and `src/app/base.css` ships the rule. The E2E CSP check catches a rename.
- A feature that needs a browser permission adds it to `Permissions-Policy` in its own change.

## Tests

- **Seam 1** (`UiServingTests`, `SecurityHeadersTests`): the in-process host on a fixture web root.
- **Seam 2** (`update-notice.test.tsx`): the notice, with `renderApp`'s fake `updates` in place of the registration.
- **Seam 3** (`app-shell.test.ts`, `register-service-worker.test.ts`): the service worker's routes and take-over on an in-memory cache, and its registration and update check.
- **Seam 4** (`src/Anichron.UI/e2e/`): Playwright against the built image (`src/docker-compose.e2e.yml`, Caddy on HTTPS because the cookies are `Secure`). It checks headers, caching, CSP and Trusted Types violations, other-origin requests and axe; `home-screen.spec.ts` checks the head tags, the manifest and its icons. Users are created only through the public API, and the compose file raises the rate limits.
- `offline.spec.ts` checks an offline reload and `update.spec.ts` the update notice, in Chromium only. Playwright cannot route an update's script request, so Caddy serves a next build's `sw.js` to a context with the `anichron-e2e-build=next` cookie (`docker-compose.e2e.yml`). Chromium fetches no service worker over a certificate it does not trust, so its project ignores certificate errors (`playwright.config.ts`).
