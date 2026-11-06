# UI design system

The tokens, themes and base components every screen of `Anichron.UI` is built from (ADR 0018,
#346). Decided on #346 on 2026-11-06; #346's children build it, and update this file where the
build settles a detail.

## Stack

- **Tailwind CSS v4** through its Vite plugin. Component variants are written with
  `tailwind-variants`: typed variants, and a class a screen passes overrides the component's
  default instead of being added next to it.
- **React Aria Components** for every interactive control, styled from Adobe's Tailwind starter.
  Its built-in strings are cut to `en` and `de` by `@react-aria/optimize-locales-plugin`; a new
  language is added there and to the catalog. The app is wrapped in `I18nProvider` with the locale
  from i18next's current language, and `<html lang>` follows the same value.
- **Icons:** `lucide-react`, imported per icon. A meaningful icon's label comes from the catalog; a
  decorative one is `aria-hidden`.
- **Fonts:** the system font stack. No font files in v1.0.
- **Class order:** `prettier-plugin-tailwindcss`, checked by `pnpm format`. No Tailwind ESLint
  plugin: an off-palette class does not exist (see Tokens).

## The look

Photo-first: the chrome is warm neutral grey, so the photos carry the colour. One accent, used
only for primary actions, focus and selection. About 12 px radius on fields and cards, pill-shaped
buttons, at least 44 px touch targets, a small type scale. The story player is black in both
themes.

## Tokens

Two layers:

- **Primitives:** the raw scales (neutral, accent, danger) as plain CSS variables. No utility
  class reads them.
- **Semantic tokens**, the only ones registered with Tailwind's `@theme`: `surface`,
  `surface-raised`, `fg`, `fg-muted`, `border`, `accent`, `on-accent`, `danger`, `focus`, and
  `player`, which is black in both themes. Tailwind's default palette is cleared
  (`--color-*: initial`).

Radius (`field`, `card`) and type size get semantic tokens the same way; spacing keeps Tailwind's
numeric scale. A new kind of colour starts as a semantic token with a value for each theme.

## Themes

The semantic tokens take their light or dark values from `prefers-color-scheme`, so screens need
no `dark:` classes. They are written so an attribute on `<html>` can override the system choice.

The System / Light / Dark toggle ships with #120, its first caller. The choice is stored per
device in `localStorage`. Applying it before the first paint needs a blocking script, and the CSP
(`script-src 'self'`) forbids an inline one: a second small Vite build compiles a TypeScript entry
into an unhashed `/theme-init.js` (served `no-cache`, like `index.html`), which shares its storage
key and values with the toggle.

## Motion and focus

- **Motion:** durations are tokens (`--duration-fast`, `--duration-base`) that resolve to `0ms`
  under `prefers-reduced-motion: reduce`; no component checks the preference itself. The story
  player's gestures follow it through Motion's `MotionConfig reducedMotion="user"` (#348).
- **Focus:** a 2 px ring in the accent, offset 2 px, on keyboard focus only (`:focus-visible`,
  React Aria's focus-visible state), never on a tap. It reaches 3:1 against both surfaces; where
  the accent does not, `focus` gets its own value in that theme.

## Base components

- They live in `src/shared/ui/`. Variant definitions stay in the view file, outside UI mutation
  (ADR 0014); logic goes into a `.ts` module and is mutated.
- They are form-agnostic: plain props, a field's error given as a message key. A feature connects
  react-hook-form through `Controller`.
- Each component ships with its first caller. #346 ships the button, the text field (label, input,
  field error) and the form-level alert, with the login screen rebuilt on them.

## Checks

- **Seam 2:** a component is tested through the screen that uses it (`renderApp` with MSW), with
  axe in light and dark, and its keyboard operation through `@testing-library/user-event` (React
  Aria's press handling ignores bare `fireEvent` events).
- **Contrast:** a unit test reads the token stylesheet and checks each semantic foreground and
  background pair in each theme: 4.5:1 for text, 3:1 for the focus ring and borders.
- **E2E:** axe in both colour schemes.
- **VoiceOver:** the maintainer runs this checklist on the PR that introduces a component:
  1. Every control announces its role, name and state (disabled, invalid, pressed, expanded).
  2. A field announces its label, and its error when it has one.
  3. An alert that appears is announced without moving focus.
  4. Reading order matches the visual order.
  5. Decorative icons are skipped.

## Reviewing the look

- **Design page:** a separate HTML entry that only the Vite dev server serves, not in the
  production build and not a route of the app's router. It shows the palette in both themes, the
  type scale and every base component in each state.
- **Screenshots:** a PR that changes the look attaches Playwright screenshots of the affected
  screens in light and dark, on an iPhone and a Pixel.
- **Accent:** the PR that introduces it shows three candidates (warm amber, coral, teal) with their
  contrast ratios; the maintainer picks one and the others are removed before merge.
- **No Storybook in v1.0.** Revisit at about 10 base components, or when someone other than an
  agent works on them.

## Budgets

The initial JS stays within 150 kB gzip and the CSS within 15 kB (`.size-limit.json`). A PR that
would exceed one says so and asks the maintainer instead of raising the limit.
