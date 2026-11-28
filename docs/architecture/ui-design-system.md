# UI design system

The tokens, themes and base components every screen of `Anichron.UI` is built from (ADR 0018).

## Stack

- **Tailwind CSS v4** with `tailwind-variants`. Components take `tv` from `src/shared/ui/tv.ts`; its tailwind-merge config reads the size and radius tokens from `tokens.css`, which it would otherwise misread as colours.
- **React Aria Components** for every interactive control; `react-aria`'s hooks (`usePress`) where a control is not a component. Its strings are cut to the app's languages (`@react-aria/optimize-locales-plugin`), so a new language is added there and to the catalog.
- **Motion** (`LazyMotion` with `domMax`) for the story's swipes, drag-to-close and the fade of a video's pause circle; it loads only with the story's route.
- **Icons:** `lucide-react`, one import per icon. A meaningful icon gets a label from the catalog; a decorative one is `aria-hidden`.
- **Fonts:** the system font stack.
- **Class order:** `prettier-plugin-tailwindcss`.

## The look

- Photo-first: warm neutral grey chrome, one accent (teal) used only for primary actions, focus and selection.
- About 12 px radius, pill buttons, touch targets of at least 44 px.
- The story player is black in both themes. Its Live Photo button and memory actions sit on grey frosted pills (`player-glass`).
- A standalone screen (sign-in, `CentredPage`) puts a frosted glass card over a backdrop of colour glows. Fields and buttons on it stay solid. Without blur support the card falls back to a solid surface.

## Tokens

- **Primitives** (`--neutral-*`, `--accent-*`, …) are plain CSS variables in `src/shared/ui/tokens.css`. No utility class reads them.
- **Semantic tokens** are the only ones registered with Tailwind (`surface`, `fg`, `accent`, `danger`, `focus`, `player`, …). Tailwind's default palette is cleared, so an off-palette class generates nothing.
- Radius and type size follow the same split. Spacing keeps Tailwind's scale.
- A new kind of colour starts as a semantic token with a value for each theme.
- Glass is a set of utilities in `src/app/glass.css`, built only from tokens. The focus ring is `focus-ring` (and `focus-ring-inset`) in `src/app/focus.css`.

## Themes

- Each semantic token is a `light-dark()` of two primitives, so screens need no `dark:` classes.
- `data-theme` on `<html>` sets `color-scheme` and so overrides the system choice; without it, the device decides.
- The System / Light / Dark choice is stored per device (`src/shared/theme.ts`, which owns the storage key), followed across tabs through the `storage` event, and System when storage is unavailable.
- `/theme-init.js` applies it before first paint: a nested Vite build (`vite.theme-init.config.ts`) of `src/app/theme-init.ts`, because the CSP forbids an inline script. Plugins in `vite.config.ts` emit it into the build and serve it under `pnpm dev`.
- A Light or Dark choice puts a `theme-color` tag without `media` first, copying that scheme's colour from its own tag; System removes it.
- The surface colours (`--neutral-50`, `--neutral-950`) are repeated as literals in `index.html`'s `theme-color` metas and the manifest (`ui-serving.md`, "The Home Screen files"), since static files cannot read a CSS variable. Changing a surface changes them too.

## The shell

The signed-in frame, a layout route in `src/app/` (`Shell.tsx`). Login and the forced password change stay outside it.

- **Every screen** has the bottom bar: a floating glass pill on `env(safe-area-inset-bottom)`, or just above the edge without one (the viewport extends under the safe areas, `index.html`), a `<nav>` of links marked with `aria-current`. Its items are listed once, in `BottomBar.tsx`.
- **The bottom bar's space is its own:** nothing floats on it. A floating control sits at `--above-bottom-bar` (`above-bottom-bar`, `src/app/bars.css`), a gap above the bar, and glides into the bar's place while it hides, with the bar's duration.
- **Hiding:** `use-hides-on-scroll.ts` hides the bar past a short scroll down and shows it on any scroll up, at the top, on a page that cannot scroll, and when focus enters it.
- **Every screen below Home** also has the top bar: its title, and a back control when it has a parent, from the route's `staticData.screen` (title key and parent path) in `router.tsx`. A bottom bar destination has no parent.
- **The top bar's look:** flat and full-width, fixed with its height kept in the flow, running up under the status bar. At rest it is the page colour, like the `theme-color`; once content scrolls under it (`use-scrolled.ts`), it is nearly solid glass (`glass-bar`, `--glass-bar-opacity`).
- **Actions:** a screen puts its own controls in the top bar through `TopBarActions` (`src/shared/top-bar.ts`), leading or trailing; a leading one takes the place of the back control while it is shown.
- **Back rule** (`back.ts`): when the previous history entry is the parent, back goes back in history; otherwise it replaces the current entry with the parent. So the back control, Android's back button and Safari's edge swipe agree.
- On wide screens the same layout is centred at a readable width (`Shell.tsx`).

## Motion and focus

- Transition durations are tokens that become `0ms` under `prefers-reduced-motion`; looping animations are `none` there, and the story's progress fill (`--animate-story-fill`) steps.
- Motion, which only the story loads, follows it through `MotionConfig reducedMotion="user"`. The pull to close reads it itself (`src/shared/reduced-motion.ts`) and only fades the backdrop; the removed list's drag-select jumps instead of gliding. A Live Photo never plays by itself, only from its button or a hold.
- A loading ring stops spinning (`motion-reduce:`).
- The Live pill and the years-ago line stay visible through a hold that hides the other controls. The pill shows a turning `player-accent` arc while its video loads (`live-ring`), faded in after `--delay-live-ring` so a quick load shows none, and pulses its icon while it plays (`live-blink`). Under reduced motion the arc stands still and the icon dims.
- A skeleton (`skeleton` utility) fades in after `--delay-skeleton`, so a quick load shows none, then pulses through `--animate-skeleton`; under reduced motion it stands still.
- Focus is an accent ring on keyboard focus only; the story's full-screen tap zones and rows draw it inside. A `tv` state takes it from `focusVariant` (`tv.ts`). A control with the ring does not transition its colours.

## Base components

- They live in `src/shared/ui/`. Variant definitions stay in the view; logic goes into a `.ts` module (ADR 0014).
- They are form-agnostic: `FormField` connects react-hook-form through `Controller` and passes `field.ref` as `inputRef`, and `FormStatus` takes plain values (a message key, `retryIn`).
- States are `tv` variants named after React Aria's render props. A state that only interaction reaches gets an exported style function, so the design page can show it.
- A caller's `className` goes on the outer element.
- Toasts (`src/shared/toasts.ts`, `Toast.tsx`): one region per layer, because a full-screen modal makes everything outside it inert. The shell's queue is `toasts` in the router context, rendered by `Shell`, so a toast outlives a navigation; a full-screen modal (the story) owns its own. A toast is announced politely and lasts a few seconds.
  - The region (`toast-region`, `bars.css`) sits above the bottom bar, or above the control a screen marks `data-floating` (`bars.css`).
- Modals (`Modal.tsx`): a titled dialog over a dimmed backdrop. Its caller renders it while open; Escape and a tap outside close it, so every modal has a way out. An `alert` one (`alertdialog`) asks before an irreversible action: only Escape or its own buttons close it, and it focuses the safe choice.
- Cards (`Card.tsx`): the glass panel, padded or, for rows, unpadded. `CentredPage.tsx` puts one, with a title and body, over the glow backdrop.
- Form status (`FormStatus.tsx`): a form's alert and its retry countdown.
- Rows (`Row.tsx`): a full-width entry in a card: a link with a chevron, a button with an icon and a chevron, or a red button.
- Skeletons (`Skeleton.tsx`): an `aria-hidden` block in the `skeleton` colour. A screen composes its own skeleton beside the view it stands in for, and marks the region it fills `aria-busy`.
- Each component ships with its first caller.

## Checks

- **Seam 2:** a component is tested through its screen, with axe, and its keyboard use through `user()` (`src/test/time.ts`). jsdom has no colour scheme, so contrast is checked elsewhere.
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
