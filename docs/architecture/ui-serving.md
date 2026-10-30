# UI serving

How the API container serves `Anichron.UI` on the same origin as `/api/v1` (ADR 0012, #359), and
the headers every response carries.

## The image

The API Dockerfile builds the UI in a Node stage on `$BUILDPLATFORM` (the bundle is the same files
for every target arch) and copies `dist/` to `/app/wwwroot`, the web root. The Node tag matches
`.nvmrc` exactly, so pnpm's `devEngines.runtime` never downloads another Node, and the stage
installs the pnpm version in `packageManager`. These pins move together: a Node bump changes the
`FROM` tag, `.nvmrc`, `engines` and `devEngines.runtime.version` in one change, and a pnpm bump
changes `packageManager` and the Dockerfile's `pnpm@` version. Dependabot's `docker` entry rewrites
only the `FROM` line, so its PR gets the rest added before it merges. A
`dotnet run` without a built UI has no `wwwroot`: the API serves as usual and every UI path is a
`404`. Development goes through `pnpm dev`, which proxies `/api` to the local API.

## The serving contract

`UseUi` (`Anichron.API/Infrastructure/UiServing.cs`) is `UseStaticFiles` on the web root plus one
middleware that runs after routing, only for a request no endpoint matched:

| Request | Response |
|---|---|
| A file under `/assets/` (Vite's hashed output) | the file, `Cache-Control: public, max-age=31536000, immutable` |
| Any other file in the web root (`index.html`, the service worker, the manifest) | the file, `Cache-Control: no-cache` |
| `GET` or `HEAD` of a path with no file extension, outside `/api` | `index.html`, `no-cache` (the client router takes over) |
| An unknown path under `/api` (any method, extension or not) | `404`, `application/problem+json` |
| An unknown path with a file extension, or a non-`GET`/`HEAD` app path | `404`, empty body |
| A known `/api` route with the wrong method | `405`, as before |

The fallbacks are middleware rather than `MapFallback` endpoints on purpose: a catch-all endpoint
matches every method, so routing would pick it instead of answering `405` for a known route.
`/api` is a path segment (`StartsWithSegments`), so `/apiary` is an app path. A dot in the last segment counts as an extension (`Path.HasExtension`), so a client route never ends in one (`/people/j.doe` would be a `404`). The web root is read
through ASP.NET Core's file provider, never `IFileSystem` (ADR 0007, `api-reads-no-file-system`).

## Security headers

`UseSecurityHeaders` (`SecurityHeaders.cs`) is the first middleware and sets the headers in
`OnStarting`, so every response gets them, including `401`, `429`, the must-change-password `403`
and the fallback `404`s.

- **Outside `/api`:** `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self'; manifest-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; require-trusted-types-for 'script'`,
  `Referrer-Policy: same-origin`, `X-Content-Type-Options: nosniff` and
  `Permissions-Policy: accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()`.
- **Under `/api`:** `X-Content-Type-Options: nosniff` and `Content-Security-Policy: frame-ancestors 'none'`.

The policy has no `Development` branch: only the Vite dev server, which sends no CSP, is looser.
Today's bundle runs under Trusted Types in all three browsers, so
`require-trusted-types-for 'script'` stays. It has no route chunks yet, so Vite's dynamic-import
preload helper is not in it; the smoke test fails on any Trusted Types violation, so the first lazy
route proves the helper too. A feature that needs a browser permission (fullscreen
for the flashback player, say) adds it to `Permissions-Policy` in its own change.

## Tests

- **Seam 1** (`Anichron.API.Tests.Unit/Infrastructure/UiServingTests.cs`, `SecurityHeadersTests.cs`):
  the in-process test host on a fixture web root (`Infrastructure/TestWebRoot/`), asserting status,
  content type, `Cache-Control` and exact header values. A web root that does not exist stands for
  "no UI built".
- **Seam 4** (`src/Anichron.UI/e2e/`): Playwright against the image built from the checkout,
  started by `src/docker-compose.e2e.yml` with PostgreSQL on tmpfs and no Worker. Projects: WebKit
  on an iPhone profile, Chromium on a Pixel profile, Firefox at a phone viewport. The smoke test
  loads the app (a visitor without a token lands on `/login`) and asserts the headers, the assets'
  caching, no CSP or Trusted Types violation, no request to another origin, and no axe violations.
  `login.spec.ts` signs in through the screen: success, a wrong password, a disabled account and a
  locked account's countdown, each with no CSP violation. Users are seeded only through the public
  API (#360): the global setup rotates the bootstrap admin's first password (so it fails on a
  database that is not fresh), and each test creates its own user with the admin API and changes
  its temporary password. Every request reaches the API from the one Docker bridge address, so the
  compose file raises `RateLimit__SensitivePermitLimit`; lockout keeps its production timings. CI
  runs both in the `E2E` job.
