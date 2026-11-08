# UI design system

The tokens, themes and base components every screen of `Anichron.UI` is built from (ADR 0018).

## Stack

- **Tailwind CSS v4** with `tailwind-variants`. Components take `tv` from `src/shared/ui/tv.ts`; its tailwind-merge config knows the size and radius tokens, which it would otherwise misread as colours.
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

- **Primitives** (`--neutral-*`, `--accent-*`, …) are plain CSS variables in `src/app/tokens.css`. No utility class reads them.
- **Semantic tokens** are the only ones registered with Tailwind (`surface`, `fg`, `accent`, `danger`, `focus`, `player`, …). Tailwind's default palette is cleared, so an off-palette class generates nothing.
- Radius and type size follow the same split. Spacing keeps Tailwind's scale.
- A new kind of colour starts as a semantic token with a value for each theme.
- Glass is three utilities in `src/app/glass.css`, built only from tokens.

## Themes

- Each semantic token is a `light-dark()` of two primitives, so screens need no `dark:` classes.
- An attribute on `<html>` that sets `color-scheme` overrides the system choice.
- The System / Light / Dark choice is stored per device. It is applied before first paint by `/theme-init.js`, a separate small build, because the CSP forbids an inline script.

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
