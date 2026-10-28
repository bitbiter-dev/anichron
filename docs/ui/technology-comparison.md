# Web UI technology comparison

As of 2026-10-28. Written for the maintainer's decision on #338. Versions and project states change;
an agent implementing this re-checks anything marked *verify* before relying on it.

`Anichron.UI` is one web app holding the admin screens (Epic 6) and the Instagram Story-style
flashback experience (Epic 7). This document compares the options for each part of the stack and
recommends one. The pick is recorded on #338 and as ADR 0012.

## Already decided

Settled in the grilling session of 2026-10-28; not reopened here.

| # | Decision |
|---|---|
| Q1 | Agents build the UI under the backend's process (issues, design notes, gates); the maintainer reviews. |
| Q2 | One app: admin and flashbacks; admin routes only for admins. |
| Q3 | A static single-page app with no server of its own. It must look and feel modern, responsive and interactive. |
| Q4 | Phone-first, installable as a PWA. No native app in v1.0. |
| Q6 | Gates as strict as the backend's. Mutation testing at 100% for logic modules; view components are excluded by an ADR and covered by component and end-to-end tests. |
| Q7 | English in v1.0, German next, others possible. Every string goes through i18n from the start. |
| Q9 | The API container serves the built files, on the same origin as `/api/v1`. |
| Q10 | The code lives in `src/Anichron.UI/`, with its own CI job in the `CI gate`'s `needs`. |
| Q11/Q15 | Proxy images and videos are authenticated by an httpOnly `SameSite=Strict` cookie that only the `GET` proxy routes accept. |
| Q12 | API types are generated from the OpenAPI document (types only), checked current in CI. |
| Q13 | WCAG 2.2 AA, light and dark theme, reduced motion respected. |
| Q14 | Last two versions of evergreen browsers; iOS Safari (WebKit) is the primary end-to-end target. |
| Q16 | An own photo-first look: Tailwind, design tokens, headless components. Not Material or Fluent. |
| Q17 | PWA scope: installable, app shell cached, recently viewed media cached. No offline library, no web push (noted on Epic 9). |
| Q18 | No requests to anything but its own server, enforced by a Content-Security-Policy. |

### How options are weighed (Q20)

1. **Agent reliability**: how correctly agents write it (training data, one obvious way to do things).
2. **Maturity and longevity**: ecosystem size, stable APIs, likely to be maintained in five years.
3. **Feel on phones**: download size, smooth gestures and animation, video.
4. **Testability**, including StrykerJS mutation testing.
5. **Tooling simplicity**: fewer tools, less configuration.
6. **Raw performance and elegance.**

### Terms used below

- **SPA (single-page app)**: the browser loads one HTML page and a JavaScript bundle once; after
  that, the app draws every screen itself and only fetches data (JSON) from `/api/v1`.
- **Virtual DOM**: the framework keeps an in-memory copy of the page, recomputes it on every
  change and patches the real page with the difference (React, Vue).
- **Signals / fine-grained reactivity**: each value knows which parts of the page use it and
  updates only those, without recomputing a component (Solid, Svelte 5, Vue's refs, Angular's
  signals).
- **Compiler framework**: the framework's work happens at build time, so less code ships to the
  browser (Svelte).
- **Hydration / SSR**: rendering HTML on a server first and "waking it up" in the browser. Ruled
  out by Q3; mentioned only where a framework is built around it.
- **Headless component**: a component that brings behaviour and accessibility (keyboard, focus,
  screen-reader roles) but no visual styling; we style it ourselves (Q16).
- **Service worker**: a script the browser runs beside the page that can answer requests from a
  local cache; it is what makes an app installable and fast on repeat visits.

---

## 1. Framework

### Shortlist at a glance

| | React | Vue 3 | Svelte 5 | SolidJS | Angular | Blazor WASM |
|---|---|---|---|---|---|---|
| Language of components | TS + JSX | TS + templates (`.vue`) | TS + templates (`.svelte`) | TS + JSX | TS + templates, classes | C# + Razor |
| Rendering model | Virtual DOM | Virtual DOM + signals | Compiler + signals | Signals, no virtual DOM | Signals (+ zone.js legacy) | .NET runtime in WebAssembly |
| 1 Agent reliability | ●●●●● | ●●●●○ | ●●●○○ | ●●○○○ | ●●●●○ | ●●●○○ |
| 2 Maturity, ecosystem | ●●●●● | ●●●●○ | ●●●○○ | ●●○○○ | ●●●●● | ●●●○○ |
| 3 Feel on phones | ●●●●○ | ●●●●○ | ●●●●● | ●●●●● | ●●●○○ | ●●○○○ |
| 4 Testability (+ Stryker) | ●●●●● | ●●●●○ | ●●●●○ | ●●●○○ | ●●●●○ | ●●●○○ |
| 5 Tooling simplicity | ●●●○○ | ●●●●○ | ●●●●○ | ●●●○○ | ●●●○○ | ●●●●○ |
| 6 Performance, elegance | ●●●○○ | ●●●●○ | ●●●●● | ●●●●● | ●●●○○ | ●●○○○ |

The dots are a judgement, not a measurement; the sections below give the reasons.

### React (with Vite)

**What it is.** A UI library from Meta. Components are TypeScript functions that return JSX
(HTML-like syntax inside TypeScript); state lives in hooks (`useState`, `useEffect`). It is only
the view layer: routing, data fetching and forms come from separate libraries, which you choose.
The React Compiler (an optional build step) memoises components automatically, removing a common
class of performance mistakes.

**Pros for Anichron**
- By far the most code, documentation and answers exist for it, so agents write it most
  reliably and reviewers find the most help.
- The largest choice of exactly the libraries this app needs: the strongest accessible headless
  components (React Aria Components, which also ships translations including German), gesture
  and animation (Motion), server-state caching (TanStack Query), i18n, OpenAPI clients.
- First-class testing: Vitest + Testing Library, Playwright, StrykerJS mutation of `.ts`/`.tsx`.
- Very stable: code written years ago still runs; Meta and a large community keep it alive.
- If a native app is ever wanted, React Native reuses the knowledge (and some logic).

**Cons for Anichron**
- "Choose your own libraries" means many decisions up front and many ways to do the same thing.
  Without written conventions agents drift (two data-fetching styles, effects used for
  everything). Mitigation: this document fixes the choices; lint rules enforce them.
- Hooks have footguns (stale closures, effect loops) that a reviewer new to UI will not spot.
  Mitigation: keep logic in plain `.ts` modules tested by unit and mutation tests, keep components
  thin, enable the React hooks lint rules.
- A larger runtime than Svelte or Solid, so a slightly heavier first load. Small in absolute terms
  for an installed PWA whose shell is cached.
- Much of React's own documentation now steers towards server frameworks (Next.js); a plain Vite
  SPA is fully supported but not what the homepage advertises.

### Vue 3 (with Vite)

**What it is.** A progressive framework led by Evan You and a community core team. Components are
single-file `.vue` files with a template, a script and styles. Reactivity is built in (refs,
computed values); router (Vue Router), state (Pinia) and i18n (vue-i18n) are official companions.

**Pros for Anichron**
- Templates read close to plain HTML; easier for a reviewer new to UI than JSX with hooks.
- Official router, state and i18n packages: fewer choices, more consistency.
- Fine-grained reactivity avoids most of React's re-render pitfalls.
- Good tooling: Vite comes from the same author; StrykerJS documents a Vue setup with Vitest.

**Cons for Anichron**
- Smaller ecosystem than React for the hard parts: accessible headless components (Reka UI,
  Ark UI) are good but less complete than React Aria; fewer gesture/animation choices.
- Two component styles exist (Options API and Composition API); agents mix them unless told.
- Template type-checking needs `vue-tsc`, an extra tool; editor support is good but separate.
- Less training data than React, so agents are good but not as reliable.

### Svelte 5 (Vite, or SvelteKit in static mode)

**What it is.** A compiler: `.svelte` files are turned into small, direct DOM code at build time,
so little framework code ships. Svelte 5 introduced "runes" (`$state`, `$derived`, `$effect`) as
its reactivity model. SvelteKit is its application framework; with the static adapter it produces
a plain SPA. Transitions and animations are built into the language.

**Pros for Anichron**
- Smallest bundles and fastest updates of the mainstream options: the best fit for criterion 3.
- Built-in transitions and animations suit the story player without an extra library.
- Very little boilerplate; components are short and readable.
- StrykerJS mutates `.svelte` files (since StrykerJS 8) with the Vitest runner.

**Cons for Anichron**
- Svelte 5 changed the syntax late in 2024. Much of the code agents learned from is Svelte 4, so
  they mix old (`$:`, `export let`) and new (runes) styles. This hits criterion 1 directly.
- Smaller ecosystem: headless components (Bits UI, Melt UI, Ark UI) are younger; fewer i18n and
  gesture choices; some libraries lag Svelte 5.
- SvelteKit pulls towards server rendering; in static mode some of its features do not apply,
  which is a source of confusion.

### SolidJS

**What it is.** JSX like React, but components run once and signals update the exact DOM nodes
that depend on them; there is no virtual DOM. Among the fastest frameworks in benchmarks.

**Pros for Anichron**
- Excellent runtime performance and small bundles.
- JSX looks like React, so much React knowledge transfers.

**Cons for Anichron**
- Looks like React but behaves differently: destructuring props or reading a signal in the wrong
  place silently breaks reactivity. Agents trained mostly on React make exactly these mistakes,
  and a reviewer new to UI will not see them. This is the weakest fit for criterion 1.
- The smallest community and ecosystem of the six; fewer maintained headless, i18n and testing
  libraries (Kobalte, Ark UI exist).
- Less certainty about long-term direction than React, Vue or Angular.

### Angular

**What it is.** A complete framework from Google: router, forms, HTTP client, dependency
injection, i18n and testing setup are all built in, generated and upgraded by the Angular CLI.
Recent versions moved to standalone components, signals and an optional zone-less mode, and to
Vitest as the test runner (*verify* the current default).

**Pros for Anichron**
- Batteries included and opinionated: one way to do most things, which keeps agents consistent.
  Dependency injection and services will feel familiar from ASP.NET Core.
- Predictable release and support schedule; `ng update` automates migrations.
- The Angular CDK provides headless accessibility primitives.

**Cons for Anichron**
- The most concepts to learn and review (modules or standalone, DI, RxJS observables and signals
  side by side, decorators); the heaviest bundles of the JavaScript options.
- Its built-in i18n compiles one build per language; switching language at runtime needs a third-
  party library (Transloco).
- The ready-made component library (Angular Material) is Material Design, which Q16 rules out;
  the CDK alone means building more by hand.
- Fewer modern gesture and animation libraries; Angular's own animation package is being
  replaced by plain CSS (*verify*).

### Blazor WebAssembly

**What it is.** Microsoft's way to write browser UI in C#: a .NET runtime compiled to WebAssembly
runs in the browser and executes Razor components. The backend's DTOs and validation could be
shared directly.

**Pros for Anichron**
- One language for the whole repository; the existing analyzers, `dotnet format`, xUnit (with
  bUnit for components) and Stryker.NET apply to component code.
- No generated TypeScript types: the API's C# records are shared directly (Q12 becomes trivial).
- A PWA project template exists.

**Cons for Anichron**
- **Download and start-up**: the browser must download and start the .NET runtime and the app's
  assemblies before anything shows. Even trimmed, this is several times a typical JavaScript SPA
  (*verify* with a measured .NET 10 build). This works directly against "phone-first, feels
  modern" (Q3, Q4).
- Gestures, video, service-worker caching and the Story player need JavaScript interop anyway, so
  the UI would be two languages after all.
- Headless components are scarce; the popular libraries (MudBlazor, Fluent UI Blazor) are
  Material or Fluent, which Q16 rules out.
- The CSP (Q18) needs `'wasm-unsafe-eval'` in `script-src`.
- Stryker.NET mutates the C# in components but not Razor markup; the same logic/view split
  applies.
- Far less Blazor code exists than React code, so agents are less reliable at it than at C#
  backend code.

### Dropped, and why

| Option | Why not |
|---|---|
| Next.js, Nuxt, Remix/React Router framework mode, Analog | Built around server rendering, ruled out by Q3. Their static export is a heavier Vite with features that do not apply. |
| htmx, Hotwire/Turbo, Alpine.js | Need HTML rendered on the server or are too small for a gesture-driven app. |
| Lit, plain Web Components | Too low-level for an app this size; few application libraries. |
| Qwik | Its advantage (resumability) is about server rendering; small ecosystem. |
| Preact | React-compatible and smaller, but the compatibility layer is a source of subtle bugs; React's size is not our bottleneck. |
| .NET MAUI Blazor Hybrid, React Native, Flutter | Native apps; Q4 says web PWA for v1.0. |

### Recommendation: React

React wins the two criteria ranked highest (agent reliability, maturity) clearly, and ties for
testability. It has the strongest library for the hardest non-functional requirement, accessible
and internationalised components (React Aria Components). Its weaknesses are a slightly larger
runtime (criterion 3) and many ways to do things, which this document and lint rules close.

**Runner-up: Svelte 5.** Best on phones and the most pleasant to write; it loses on criterion 1
because agents still mix Svelte 4 and 5, and on the ecosystem for accessible components. If agent
reliability were ranked below phone feel, Svelte would win.

**Third: Vue 3.** A sound, consistent choice with official companions; it trails React on the
accessible-component and animation ecosystem without being clearly better elsewhere.

The rest of this document assumes React. Where another framework would change a choice, the
equivalents are in [Equivalents for other frameworks](#equivalents-for-other-frameworks).

---

## 2. Foundation

### Language: TypeScript, strict

TypeScript with `strict`, `noUncheckedIndexedAccess` and `noImplicitOverride`. Plain JavaScript is
not considered: types are what lets the generated API types (Q12) catch drift and what makes
agents' mistakes fail the build.

### Build tool

| Option | Notes |
|---|---|
| **Vite** (recommended) | The standard for React, Vue, Svelte and Solid. Instant dev server, production build by Rolldown (Vite 8, March 2026). Large plugin ecosystem (PWA, i18n, OpenAPI). |
| Rsbuild / Rspack | Fast, webpack-compatible; useful for migrating large webpack apps, which we do not have. |
| webpack | Legacy; slower and more configuration. |
| Angular CLI | Only with Angular (uses esbuild/Vite internally). |

### Package manager and Node

| Option | Notes |
|---|---|
| **pnpm** (recommended) | Fast, disk-efficient, and strict: a package can only import what it declares, which stops "works on my machine" dependency bugs. Lockfile in the repo; version pinned in `package.json`'s `packageManager` field. |
| npm | Ships with Node, zero setup; looser `node_modules` lets code import undeclared packages. Fine, but less strict. |
| Yarn (Berry) | Plug'n'Play mode breaks some tools; little gain over pnpm. |
| Bun | Very fast, but a different runtime; Vitest, Stryker and Playwright are proven on Node first. |

**Node version:** the current Active LTS, pinned in `.nvmrc` and `engines`. Node 26 enters Active
LTS on 2026-10-28 (supported to April 2029); Node 24 is in maintenance until April 2028. Use 26
unless a tool in this stack does not support it yet (*verify* at implementation). Newer Node
versions no longer bundle Corepack, so CI installs pnpm explicitly (`pnpm/action-setup`).

---

## 3. App structure

### Routing

| Option | Pros | Cons |
|---|---|---|
| **TanStack Router** (recommended) | Fully type-safe paths, params and search params (`?status=…&page=…` for the admin lists); loaders integrate with TanStack Query; a wrong link fails the build. | Newer than React Router, so somewhat less training data. |
| React Router (declarative/library mode) | The most widely used; agents know it well. | Type safety is weaker in library mode; its development focus is framework mode (server rendering). |
| Wouter | Tiny. | Too little for guards, nested layouts and typed search params. |

Type-safe routes turn an agent's wrong link or missing param into a compile error, which matters
more than the training-data edge of React Router.

### Server state and data fetching

**TanStack Query** (recommended). It caches API responses, de-duplicates requests, retries,
refetches when the app comes back to the foreground, and supports optimistic updates (star, like
and hide in #53 should feel instant and roll back on error) and prefetching (#54: load the next
memory before it is needed). Alternatives: SWR (simpler, fewer features), RTK Query (needs Redux),
hand-written `fetch` in effects (the usual source of race conditions; ruled out).

The query cache is cleared on logout, like the media cache (see PWA).

### Client state

Little is needed: the signed-in user and access token (a small module plus React context), UI
state local to components. **No global store** in v1.0. If one becomes necessary, Zustand (small,
simple). Redux is not considered.

### Forms and validation

| Option | Pros | Cons |
|---|---|---|
| **React Hook Form + Zod** (recommended) | The most common pairing; performant; Zod schemas give typed form values and messages. | Two libraries. |
| TanStack Form | Very type-safe, same family as Router and Query. | Younger, less training data. |
| Plain `useState` forms | No dependency. | Validation, dirty state and errors re-invented per form. |

The API stays the authority: its `ProblemDetails` validation errors are mapped onto form fields.
Valibot is a smaller alternative to Zod if bundle size ever matters.

### API client (Q12)

| Option | What it generates | Notes |
|---|---|---|
| **openapi-typescript + openapi-fetch** (recommended) | Types only; a very small typed `fetch` wrapper reads them. | Least generated code; every request and response is checked against the spec. `openapi-react-query` adds typed TanStack Query helpers. |
| Orval | Full React Query hooks and clients. | Much more generated code in diffs; harder to customise auth and errors. |
| Hey API (`@hey-api/openapi-ts`) | SDK functions and types. | Good, but an SDK we do not need. |
| Kiota (Microsoft) | A request-builder SDK. | Verbose for a small app; natural only with Blazor. |
| NSwag | TypeScript client classes. | Older style; heavier. |

**Backend prerequisites** (their own issue, see [Backend work this creates](#backend-work-this-creates)):
- Generate the OpenAPI document at build time with `Microsoft.Extensions.ApiDescription.Server`
  (`OpenApiGenerateDocuments`, an explicit `OpenApiDocumentsDirectory`), check it in, and fail CI
  when it is stale, as `docs/schema.sql` does. Generation runs the API's entry point
  (`dotnet getdocument`), so start-up must not migrate or need a database while the document is
  being generated.
- Describe every response: `TypedResults` or `Produces<T>`, and `ProblemDetails` instead of
  anonymous objects such as `new { error = … }`, which appear untyped in the spec.

### Authentication in the browser

- Access token in memory only (never `localStorage`); refresh through the existing httpOnly
  refresh cookie (`POST /api/v1/auth/refresh`). One refresh at a time: concurrent `401`s wait for
  the same refresh, then retry once; a failed refresh signs out.
- Media (`<img>`, `<video>`) authenticate through the proxy cookie (Q15); nothing else does.
- The admin work-item stream (Server-Sent Events) needs the `Authorization` header, which the
  browser's `EventSource` cannot send. A small own module reads the stream with `fetch` and a
  stream reader (reconnect with `Last-Event-ID`, merge by id), unit- and mutation-tested. The
  `@microsoft/fetch-event-source` package does this too but has had no release in years
  (*verify*).

### Internationalisation (Q7)

| Option | Pros | Cons |
|---|---|---|
| **react-i18next** (recommended) | The most used; JSON catalogs per language; lazy-loads namespaces; typed keys via TypeScript; plural rules for German built in. | Its own message format by default (ICU via a plugin). |
| Lingui | Compile-time catalogs, ICU messages, small runtime, extraction CLI. | Macros add a build step; less training data. |
| FormatJS (`react-intl`) | The ICU standard, `Intl`-based. | More verbose API. |
| Paraglide JS | Compile-time, tree-shaken, typed message functions, framework-agnostic. | Newest; least training data. |

Rules that apply whichever is chosen: no hard-coded user-facing string (a lint rule), dates and
numbers through the browser's `Intl` API (not a date library), a CI check that every language has
every key once German exists. React Aria Components already ships German translations of its own
labels.

---

## 4. Look and feel

### Styling (Q16)

| Option | Pros | Cons |
|---|---|---|
| **Tailwind CSS** (recommended, decided) | Utility classes in the markup; design tokens as CSS variables (`@theme`), dark mode built in; outputs one static CSS file (CSP-safe); agents write it very reliably. | Long class lists; variants need a helper (`tailwind-variants` or `cva` with `tailwind-merge`). |
| CSS Modules + CSS variables | Plain CSS, scoped per component. | More files; consistency depends on discipline. |
| vanilla-extract | Typed, zero-runtime CSS in TypeScript. | Smaller community; extra build plugin. |
| Runtime CSS-in-JS (Emotion, styled-components) | Co-located styles. | Runtime cost on phones; injects `<style>` tags, which a strict CSP must then allow. Not recommended. |

### Headless component library

| Option | Pros | Cons |
|---|---|---|
| **React Aria Components** (Adobe, recommended) | The strongest accessibility, tested with screen readers; built-in translations (German included) and locale-aware dates; press and long-press handling that smooths over iOS touch quirks; used in Adobe's own products. | More verbose than Radix-style APIs; fewer copy-paste examples than shadcn/ui. Adobe publishes Tailwind-styled starter components to begin from. |
| Base UI (from the Radix and Material UI teams) | Modern API, v1 since December 2025, reported as shadcn/ui's default since mid-2026 (*verify*). | No translations of its own; fewer components (no date pickers). |
| Radix Primitives | Huge adoption, the base of most shadcn/ui code. | Development reported to have slowed; long-open issues (*verify*). |
| shadcn/ui | Ready, Tailwind-styled components copied into the repo (on Radix or Base UI). | Fast start, but the app looks like many other shadcn apps; Q16 wants its own look. Useful as a reference. |
| Ark UI | State-machine based, same API for React, Vue, Solid, Svelte. | Smaller React community. |
| Headless UI (Tailwind Labs) | Simple, Tailwind-friendly. | Few components. |

### Icons and fonts (Q18)

- **Icons:** Lucide (`lucide-react`): consistent outline set, each icon a tree-shaken SVG component
  bundled into the app. Alternatives: Heroicons (fewer icons), Phosphor, Tabler.
- **Fonts:** the system font stack (San Francisco on iOS, Roboto on Android, Segoe on Windows)
  costs nothing and feels native; one display font for headings, if the design wants one, is
  bundled through Fontsource (npm), never loaded from Google Fonts.

### Animation and gestures (story player, #50–#52, #56)

| Need | Recommended | Notes |
|---|---|---|
| Story progress bar, fades, simple transitions | Plain CSS (keyframes, transitions) | GPU-friendly; hold-to-pause is `animation-play-state: paused`. Reduced motion: one media query. |
| Swipe between memories, drag to dismiss, enter/exit animations | **Motion** (motion.dev, formerly Framer Motion) | Gestures, layout and presence animations; `MotionConfig reducedMotion="user"` honours Q13. Alternative: `@use-gesture/react` + CSS. |
| Tap left/right, hold to pause | Pointer Events, or React Aria's press/long-press hooks | iOS needs `-webkit-touch-callout: none`, `user-select: none` and `touch-action` on the player, or a long press opens the system menu. |
| Burst frames (#56) | CSS scroll snap | Native swiping, no library. |

The player's timing (advance after n seconds, pause, resume, skip) is a plain state machine in a
`.ts` module driven by fake timers in tests (Vitest's fake timers), mirroring the backend's "no
real-time waits" rule. XState is an option if it grows, but not needed up front.

### Video and Live Photos (#52, #54, #55)

- Native `<video>` with the `WebVideo` H.264 MP4 proxy, which every target browser plays. No player
  library (Video.js, Vidstack): the story needs no controls bar or adaptive streaming.
- iOS rules that shape #52 and #54 whatever the framework: inline playback needs `playsinline`;
  autoplay needs `muted` or a user gesture. Hold-to-play starts on `pointerdown`, a gesture.
  Safari may ignore `preload` on cellular, so prefetching video is best-effort.
- Images: prefetch the next memory's preview with `new Image()` + `decode()`.
- BlurHash placeholder (#55): the `blurhash` package decodes `Metadata.Blurhash` to pixels drawn on
  a small `<canvas>`; CSP needs nothing extra for a canvas (`img-src blob: data:` only if converted
  to an image URL).

---

## 5. PWA (Q17)

| Option | Notes |
|---|---|
| **vite-plugin-pwa, `injectManifest` mode** (recommended) | Generates the manifest and precaches the app shell from the build; our own service-worker file (using Workbox modules) holds the media caching rules, so they are code we test. |
| vite-plugin-pwa, `generateSW` mode | Rules as configuration only; less control over logout purging. |
| Serwist | A maintained Workbox fork; similar, smaller community around Vite SPAs. |
| Hand-written service worker | Full control; re-invents precaching and update handling. |

Requirements, independent of the library:
- **Precache** the app shell (HTML, JS, CSS, icons). **Never cache `/api` JSON** in the service
  worker; TanStack Query handles that in memory.
- **Media cache:** `CacheFirst` for `/api/v1/assets/*/proxies/*` with a size and age limit.
- **Privacy on shared devices:** a cache keyed by URL would serve user A's thumbnails to user B on
  the same browser without asking the API. **Logout and user switch delete the media cache** (and
  the query cache); a test covers it.
- **Updates:** UI and API ship in one image (Q9), so an open tab with an old shell can talk to a
  newer API. A new service worker shows "a new version is available, reload" instead of updating
  silently mid-story.
- **iOS:** installation is "Add to Home Screen" (no install prompt); `apple-touch-icon` and
  standalone display in the manifest; Safari may evict caches of sites not used for weeks, so the
  cache is an accelerator, never the only copy.

---

## 6. Quality (Q6, Q13)

| Concern | Recommended | Alternatives |
|---|---|---|
| Lint | ESLint (flat config) + `typescript-eslint` strict type-checked + React hooks rules + `jsx-a11y` + a no-literal-strings rule for i18n | Biome (one fast tool for lint and format, fewer type-aware rules and plugins); oxlint (fast, complements ESLint) |
| Format | Prettier (+ Tailwind class sorting plugin) | Biome |
| Architecture rules | dependency-cruiser: e.g. only the API module calls `fetch`, views never import the service worker, feature folders do not reach into each other. The UI's counterpart of ArchUnitNET. | eslint-plugin-boundaries |
| Unit and component tests | Vitest + Testing Library (jsdom), fake timers for the player | Vitest browser mode (real browser; StrykerJS support undocumented, so not for mutated tests) |
| API mocking in tests | MSW (Mock Service Worker), typed from the generated API types | Hand-written fetch stubs |
| End-to-end | Playwright: WebKit (primary, Q14), Chromium, Firefox, with phone viewports; against the real API and PostgreSQL from `docker-compose` with fixture media | Cypress (no WebKit by default) |
| Accessibility checks | `@axe-core/playwright` in E2E, `vitest-axe` in component tests, plus a short manual keyboard and VoiceOver checklist per screen (automated checks find only part of the problems) | Lighthouse CI |
| Mutation testing | StrykerJS with `@stryker-mutator/vitest-runner`, threshold 100 | none |
| Bundle size | `size-limit` budget in CI (criterion 3) | Vite's report only |
| Component workshop | Not in v1.0 (Storybook adds maintenance) | Storybook, Ladle |

**Mutation scope (Q6).** A simple, checkable rule: logic lives in `.ts` files, views in `.tsx`.
StrykerJS mutates `src/**/*.ts` only (tests excluded); views are covered by component, E2E and
accessibility tests. The exclusion gets an ADR (as ADR 0005 does for the backend), and a
`scripts/ui-mutation-guard.sh` mirrors `mutation-guard.sh`: one config file, thresholds at 100,
`mutate` globs only as documented, every exclusion with its ADR row. StrykerJS's Vitest runner runs
tests in a single thread, so mutated suites stay in jsdom, not browser mode.

---

## 7. Delivery (Q9, Q10, Q18)

- **Build:** a multi-stage API Dockerfile: a Node stage runs `pnpm install --frozen-lockfile` and
  `pnpm build`, the output is copied into the API's `wwwroot` before `dotnet publish`. The Node
  stage runs on the build platform (`--platform=$BUILDPLATFORM`), since its output is the same for
  `amd64` and `arm64`, which avoids slow emulated builds.
- **Serving:** `MapStaticAssets` (fingerprinting and compression at publish) or `UseStaticFiles`,
  plus a fallback to `index.html` for client-side routes. **The fallback must not catch
  `/api/**`**: an unknown API path stays a JSON `404`, never `index.html` with `200`, or the
  "404, never 403" rule and the error contract break. A test pins it.
- **Cache headers:** hashed files under `/assets/` `public, max-age=31536000, immutable`;
  `index.html`, the service worker and the manifest `no-cache`.
- **ADR 0007:** serving the UI's own build output through ASP.NET Core's static-file provider does
  not touch media and does not use `IFileSystem`, so `api-reads-no-file-system` is unaffected; the
  ADR's stores stay the only way the API reads NAS or proxy files.
- **Content-Security-Policy** on HTML responses:
  `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:;
  media-src 'self' blob:; connect-src 'self'; worker-src 'self'; manifest-src 'self';
  font-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'`,
  plus `Referrer-Policy: same-origin`, `X-Content-Type-Options: nosniff` and a restrictive
  `Permissions-Policy`. Any library that injects `<style>` tags at runtime must be caught in the
  first spike (React Aria and Motion set styles through the DOM, which CSP allows; *verify*).
  The Vite dev server gets a looser policy in development only.
- **Development:** the Vite dev server proxies `/api` to `http://localhost:5207`, so development is
  same-origin too and the cookies behave as in production.
- **CI:** a `UI` job (install, typecheck, lint, format check, architecture rules, unit tests,
  scoped Stryker, build, bundle size, generated-types check) and an `E2E` job (compose up,
  Playwright), both added to the `CI gate`'s `needs`. Dependabot gets an `npm` entry; CodeQL adds
  `javascript-typescript`.

---

## Equivalents for other frameworks

If the decision is not React, these replace the React-specific picks; the rest of the document
stands.

| Concern | Vue 3 | Svelte 5 | Solid | Angular | Blazor |
|---|---|---|---|---|---|
| Routing | Vue Router | SvelteKit router or svelte-spa-router | Solid Router / TanStack Router | Angular Router | Blazor router |
| Server state | TanStack Query (Vue) | TanStack Query (Svelte) | TanStack Query (Solid) | TanStack Query (Angular) or services + signals | own services |
| Headless components | Reka UI, Ark UI | Bits UI, Melt UI, Ark UI | Kobalte, Ark UI | Angular CDK, Spartan | few; mostly hand-built |
| i18n | vue-i18n | Paraglide JS, svelte-i18n | Paraglide JS, `@solid-primitives/i18n` | Transloco (runtime) | `IStringLocalizer` |
| Animation | Vue `<Transition>`, Motion for Vue | built-in transitions | Motion One, CSS | CSS (Angular animations being phased out, *verify*) | CSS + JS interop |
| Component tests | Vue Testing Library | Svelte Testing Library | Solid Testing Library | Angular Testing Library | bUnit |
| Mutation | StrykerJS (Vitest) | StrykerJS (Vitest, `.svelte` since 8.0) | StrykerJS (Vitest) | StrykerJS | Stryker.NET (C# only, not Razor) |

---

## Recommended stack

| Layer | Pick |
|---|---|
| Framework | React, TypeScript strict, React Compiler |
| Build, packages | Vite, pnpm, Node Active LTS |
| Routing, data | TanStack Router, TanStack Query |
| Forms | React Hook Form + Zod |
| API client | openapi-typescript + openapi-fetch |
| i18n | react-i18next |
| Styling, components | Tailwind CSS + React Aria Components |
| Icons, fonts | Lucide; system font stack (+ Fontsource if a display font is wanted) |
| Motion | CSS + Motion; Pointer Events / React Aria for press and long press |
| PWA | vite-plugin-pwa (`injectManifest`) |
| Quality | ESLint + Prettier, dependency-cruiser, Vitest + Testing Library, MSW, Playwright, axe, StrykerJS, size-limit |
| Delivery | Built in the API's Dockerfile, served from `wwwroot` with an `/api`-excluding fallback, strict CSP |

**Why this stack in one paragraph.** It is the combination agents have seen most, so the
maintainer reviews behaviour rather than fights idioms; each library is the established choice in
its slot, with the exception of TanStack Router and React Aria, chosen because they turn mistakes
into compile errors and give accessibility and German translations for free. Logic sits in plain
TypeScript modules that the same 100% mutation gate as the backend can hold, and the views on top
are kept thin and tested in real browsers, with WebKit first.

## Backend work this creates

Each is its own issue once the stack is decided; the first two block Epic 7, the third blocks
Epic 6.

1. **Media cookie (Q15):** login and refresh also set an httpOnly, `Secure`, `SameSite=Strict`
   cookie with the access token; only `GET /api/v1/assets/{id}/proxies/*` accepts it; logout
   clears it.
2. **Cache-busting proxy URLs (#340):** proxy responses are `private, max-age=31536000, immutable`
   (`MediaEndpoints.ProxyCacheControl`), but the URL is `/assets/{id}/proxies/{type}`, and a row
   keeps its `Id` when it takes new content (`ContentChanged`, `OverwrittenByMove`, #250, #331).
   A browser or service worker then shows the old image for up to a year. The URL in `AssetDto`
   needs a content version (for example `?v={contentHash}`) or the header must change.
3. **OpenAPI document:** generated at build time, checked in and gated; typed responses and
   `ProblemDetails` everywhere; start-up safe to run without a database during generation.
4. **Static UI serving:** `wwwroot`, the `/api`-excluding fallback, cache headers and the CSP,
   shipped with the first UI screen (first caller).
