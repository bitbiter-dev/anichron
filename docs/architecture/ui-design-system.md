# UI design system

The tokens, themes and base components every screen of `Anichron.UI` is built from (ADR 0018,
#346). Decided on #346 on 2026-11-06; #346's children build it, and update this file where the
build settles a detail.

## Stack

- **Tailwind CSS v4** through its Vite plugin. Component variants are written with
  `tailwind-variants`: typed variants, and a class a screen passes overrides the component's
  default instead of being added next to it. Components take `tv` from `src/shared/ui/tv.ts`, whose
  tailwind-merge config names the type-size and radius tokens: without it, tailwind-merge reads
  `text-small` as a colour and drops it next to `text-danger`.
- **React Aria Components** for every interactive control, styled from Adobe's Tailwind starter.
  Its built-in strings are cut to `en` and `de` by `@react-aria/optimize-locales-plugin`, which
  matches by language, so every region of each is kept; a new language is added there and to the
  catalog. The app is wrapped in `I18nProvider` with the locale
  from i18next's current language, and `<html lang>` follows the same value.
- **Icons:** `lucide-react`, imported per icon. A meaningful icon's label comes from the catalog; a
  decorative one is `aria-hidden`. Its first caller is #120's navigation.
- **Fonts:** the system font stack. No font files in v1.0.
- **Class order:** `prettier-plugin-tailwindcss`, checked by `pnpm format`. No Tailwind ESLint
  plugin: an off-palette class does not exist (see Tokens).

## The look

Photo-first: the chrome is warm neutral grey, so the photos carry the colour. One accent, teal
(chosen on #457, 2026-11-07), used only for primary actions, focus and selection. About 12 px
radius on fields and cards, pill-shaped buttons, at least 44 px touch targets, a small type scale.
The story player is black in both themes.

Not flat (decided on #457, 2026-11-07): a screen that stands alone, such as sign-in, puts a
frosted **glass** card over a soft backdrop of three colour glows (accent, warm, cool). Fields and
buttons on the card stay solid, so typed text and labels never sit on something see-through, and
the primary button lifts off the card with a soft accent shadow. Older phones are not a target, so
the blur is not rationed; where a browser cannot blur, the card falls back to the solid raised
surface.

## Tokens

Two layers:

- **Primitives:** the raw scales (`--neutral-*`, `--accent-*`, `--danger-*`, `--peach-*`,
  `--blue-*`, `--black`) as plain CSS variables in `src/app/tokens.css`. No utility class reads
  them.
- **Semantic tokens**, the only ones registered with Tailwind's `@theme`: `surface`,
  `surface-raised`, `fg`, `fg-muted`, `border`, `accent`, `on-accent`, `danger`, `focus`, the
  backdrop glows `backdrop-accent`, `backdrop-warm` and `backdrop-cool`, and `player`, which is
  black in both themes. Tailwind's default palette is cleared
  (`--color-*: initial`). `focus` is the accent in both themes, since the accent reaches 3:1 on
  both surfaces.

Radius (`rounded-field`, `rounded-card`) and type size (`text-small`, `text-body`, `text-title`)
get semantic tokens the same way, with Tailwind's defaults cleared; `rounded-full` (the pill
button) is not a theme value and stays. Spacing keeps Tailwind's numeric scale. A new kind of
colour starts as a semantic token with a value for each theme.

Glass is three utilities in `src/app/glass.css`, built only from tokens: `glow-backdrop` (the
glows over `surface`), `glass-card` (`surface-raised` at `--glass-opacity`, blurred by
`--glass-blur`, with `--glass-edge` and `--shadow-glass`) and `lift-accent` (`--shadow-accent`).
The shadows and the edge take a `light-dark()` colour like the semantic tokens.

## Themes

The semantic tokens take their light or dark values from `prefers-color-scheme`, so screens need
no `dark:` classes. Each one is a `light-dark()` of two primitives, and `:root` declares
`color-scheme: light dark`, so the values follow the system setting at once. An attribute on
`<html>` that sets `color-scheme: light` or `dark` overrides the system choice without restating
any token.

The System / Light / Dark toggle ships with #120, its first caller. The choice is stored per
device in `localStorage`. Applying it before the first paint needs a blocking script, and the CSP
(`script-src 'self'`) forbids an inline one: a second small Vite build compiles a TypeScript entry
into an unhashed `/theme-init.js` (served `no-cache`, like `index.html`), which shares its storage
key and values with the toggle.

## Motion and focus

- **Motion:** durations are tokens (`--duration-fast`, `--duration-base`) that resolve to `0ms`
  under `prefers-reduced-motion: reduce`; no component checks the preference itself. Tailwind's
  `transition-*` utilities default to `--duration-fast`. The story player's gestures follow it
  through Motion's `MotionConfig reducedMotion="user"` (#348).
- **Focus:** a 2 px ring in the accent, offset 2 px, on keyboard focus only (`:focus-visible`,
  React Aria's focus-visible state), never on a tap. A native text input matches `:focus-visible`
  on a tap too, by browser design; React Aria's focus-visible state removes that from #458 on.
  The ring reaches 3:1 against both surfaces; an accent that does not in a theme gives `focus`
  its own value there. A control with the ring does not transition its colours: the outline would
  fade in from the text colour.

## Base components

- They live in `src/shared/ui/`. Variant definitions stay in the view file, outside UI mutation
  (ADR 0014); logic goes into a `.ts` module and is mutated.
- They are form-agnostic: plain props, a field's error given as a message key. A feature connects
  react-hook-form through `Controller`, passing `field.ref` as the text field's `inputRef` so a
  failed submit still focuses the first invalid field.
- States are `tv` variants named after React Aria's render props (`isHovered`, `isPressed`,
  `isFocusVisible`, `isDisabled`, `isInvalid`). A component with a state that only interaction
  reaches exports that part's style function (`buttonStyles`, `inputStyles`), so the design page
  can show the state.
- A caller's `className` goes on the component's outer element: the button, the text field's
  wrapper, the alert.
- The text field sets `aria-invalid` to `false` when valid, where React Aria leaves it out.
- Each component ships with its first caller. #346 ships the button, the text field (label, input,
  field error) and the form-level alert, with the login screen rebuilt on them.

## Checks

- **Seam 2:** a component is tested through the screen that uses it (`renderApp` with MSW), with
  axe, and its keyboard operation through `@testing-library/user-event` (React Aria's press
  handling ignores bare `fireEvent` events). jsdom resolves no colour scheme, so axe there checks
  names, roles and states; light and dark are the E2E run's.
- **Contrast:** a unit test (`src/app/tokens.test.ts`) reads the token stylesheet and checks each
  semantic foreground and background pair in each theme: 4.5:1 for text, 3:1 for the focus ring
  and borders. Text on glass is checked against the worst case the blur can produce: the card at
  `--glass-opacity` over each glow at full strength, and over the bare surface. axe cannot measure
  text over a blur, so this test is the contrast check for glass.
- **E2E:** axe in both colour schemes (`e2e/accessibility.ts`); `e2e/look.spec.ts` checks the
  theme switch, reduced motion, the focus ring and touch targets, and captures the screenshots.
- **VoiceOver:** the maintainer runs this checklist on the PR that introduces a component:
  1. Every control announces its role, name and state (disabled, invalid, pressed, expanded).
  2. A field announces its label, and its error when it has one.
  3. An alert that appears is announced without moving focus.
  4. Reading order matches the visual order.
  5. Decorative icons are skipped.

## Reviewing the look

- **Design page:** a separate HTML entry that only the Vite dev server serves, not in the
  production build and not a route of the app's router: `design.html`, at `/design.html` under
  `pnpm dev`, with its code in `src/design/`. It has its own Tailwind entry, and the app's
  stylesheet excludes `src/design/` from its sources, so no class of the page reaches the app's
  CSS. Its text is not in the catalog. It shows the palette in both themes with contrast ratios,
  the type scale, the glass card in both themes, and every base component in each state.
- **Screenshots:** a PR that changes the look attaches Playwright screenshots of the affected
  screens in light and dark, on an iPhone and a Pixel. CI's `E2E` job uploads them as the
  `screenshots` artifact.
- **Accent:** teal, picked from three candidates (warm amber, coral, teal) on #457: amber did not
  please, and coral sat too close to `danger`.
- **No Storybook in v1.0.** Revisit at about 10 base components, or when someone other than an
  agent works on them.

## Budgets

The initial JS stays within 170 kB gzip and the CSS within 15 kB (`.size-limit.json`). A PR that
would exceed one says so and asks the maintainer instead of raising the limit. The JS limit was
150 kB until #458, when the maintainer raised it (2026-11-08): React Aria's button and text field and tailwind-merge took the login screen from
135 kB to 165 kB. Every screen needs them, so lazy routes would split that cost rather than remove
it; they are worth adding when screens most users never open (the admin screens) arrive.
