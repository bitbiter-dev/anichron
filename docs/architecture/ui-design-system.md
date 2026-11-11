# UI design system

The tokens, themes and base components every screen of `Anichron.UI` is built from (ADR 0018).

## Stack

- **Tailwind CSS v4** with `tailwind-variants`. Components take `tv` from `src/shared/ui/tv.ts`; its tailwind-merge config reads the size and radius tokens from `tokens.css`, which it would otherwise misread as colours.
- **React Aria Components** for every interactive control. Its strings are cut to the app's languages (`@react-aria/optimize-locales-plugin`), so a new language is added there and to the catalog.
- **Icons:** `lucide-react`, one import per icon. A meaningful icon gets a label from the catalog; a decorative one is `aria-hidden`.
- **Fonts:** the system font stack.
- **Class order:** `prettier-plugin-tailwindcss`.

## The look

- Photo-first: warm neutral grey chrome, one accent (teal) used only for primary actions, focus and selection.
- About 12 px radius, pill buttons, touch targets of at least 44 px.
- The story player is black in both themes.
- A standalone screen (sign-in) puts a frosted glass card over a backdrop of colour glows. Fields and buttons on it stay solid. Without blur support the card falls back to a solid surface.

## Tokens

- **Primitives** (`--neutral-*`, `--accent-*`, …) are plain CSS variables in `src/shared/ui/tokens.css`. No utility class reads them.
- **Semantic tokens** are the only ones registered with Tailwind (`surface`, `fg`, `accent`, `danger`, `focus`, `player`, …). Tailwind's default palette is cleared, so an off-palette class generates nothing.
- Radius and type size follow the same split. Spacing keeps Tailwind's scale.
- A new kind of colour starts as a semantic token with a value for each theme.
- Glass is three utilities in `src/app/glass.css`, built only from tokens.

## Themes

- Each semantic token is a `light-dark()` of two primitives, so screens need no `dark:` classes.
- `data-theme` on `<html>` sets `color-scheme` and so overrides the system choice; without it, the device decides.
- The System / Light / Dark choice is stored per device (`src/shared/theme.ts`, which owns the storage key), followed across tabs through the `storage` event, and System when storage is unavailable.
- `/theme-init.js` applies it before first paint: a second Vite build (`vite.theme-init.config.ts`) of `src/app/theme-init.ts`, because the CSP forbids an inline script. `pnpm dev` serves it through a plugin in `vite.config.ts`.
- A Light or Dark choice puts a `theme-color` tag without `media` first, copying that scheme's colour from its own tag; System removes it.
- The surface colours (`--neutral-50`, `--neutral-950`) are repeated as literals in `index.html`'s `theme-color` metas and the manifest (`ui-serving.md`, "The Home Screen files"), since static files cannot read a CSS variable. Changing a surface changes them too.

## The shell

The signed-in frame, a layout route in `src/app/` (`Shell.tsx`). Login stays outside it.

- **Every screen** has the bottom bar: a floating glass pill above `env(safe-area-inset-bottom)` (the viewport extends under the safe areas, `index.html`), a `<nav>` of links marked with `aria-current`. Its items are listed once, in `BottomBar.tsx`.
- **Hiding:** `use-hides-on-scroll.ts` hides the bar past a short scroll down and shows it on any scroll up, at the top, on a page that cannot scroll, and when focus enters it.
- **Every screen below Home** also has the top bar: a back control and its title, from the route's `staticData.screen` (title key and parent path) in `router.tsx`.
- **Back rule** (`back.ts`): when the previous history entry is the parent, back goes back in history; otherwise it replaces the current entry with the parent. So the back control, Android's back button and Safari's edge swipe agree.
- On wide screens the same layout is centred at a readable width (`Shell.tsx`).

## Motion and focus

- Durations are tokens that become `0ms` under `prefers-reduced-motion`; no component checks the preference itself.
- Focus is a 2 px accent ring, offset 2 px, on keyboard focus only. A control with the ring does not transition its colours.

## Base components

- They live in `src/shared/ui/`. Variant definitions stay in the view; logic goes into a `.ts` module (ADR 0014).
- They are form-agnostic: a feature connects react-hook-form through `Controller` and passes `field.ref` as `inputRef`.
- States are `tv` variants named after React Aria's render props. A state that only interaction reaches gets an exported style function, so the design page can show it.
- A caller's `className` goes on the outer element.
- Each component ships with its first caller.

## Checks

- **Seam 2:** a component is tested through its screen, with axe, and its keyboard use through `user-event`. jsdom has no colour scheme, so contrast is checked elsewhere.
- **Contrast:** `src/app/tokens.test.ts` checks every semantic foreground and background pair in each theme, glass at its worst case included.
- **E2E:** axe in both schemes; `e2e/look.spec.ts` checks the theme switch, reduced motion, the focus ring and touch targets, and captures screenshots.
- **VoiceOver**, by the maintainer, on the PR that introduces a component:
  1. Every control announces its role, name and state.
  2. A field announces its label, and its error when it has one.
  3. An alert that appears is announced without moving focus.
  4. Reading order matches the visual order.
  5. Decorative icons are skipped.

## Reviewing the look

- **Design page:** `/design.html` under `pnpm dev` (`src/design/`), absent from the build. It shows the palette, type scale, glass card and every component state in both themes.
- **Screenshots:** a PR that changes the look attaches the E2E job's `screenshots` artifact (light and dark, iPhone and Pixel).
- **No Storybook** until there are about 10 base components or a human works on them.
