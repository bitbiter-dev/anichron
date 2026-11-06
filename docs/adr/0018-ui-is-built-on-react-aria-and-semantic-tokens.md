# The UI is built on React Aria Components and Tailwind semantic tokens

Status: accepted (2026-11-06, #346). Decided by the maintainer on #346 (2026-11-06).

Every screen of `Anichron.UI` is built from React Aria Components, styled with Tailwind CSS v4
through `tailwind-variants`, and coloured only through **semantic tokens**: surface, foreground,
muted foreground, border, accent, on-accent, danger, focus and the fixed player surface. The raw
colour scales are plain CSS variables that only the semantic tokens read; Tailwind's default
palette is cleared, so a class such as `bg-blue-500` generates nothing. Radius and type size
follow the same split. The rules, the look and the checks are in
`docs/architecture/ui-design-system.md`.

## Why React Aria Components

The requirement (#338, Q13, Q7, Q16) is an own look at WCAG 2.2 AA with components that carry
their own translations. React Aria has the most complete accessibility of the headless libraries
(roles, focus management, press and long-press handling that behaves the same on touch, mouse and
keyboard), ships translated strings including German and locale-aware dates, and has an official
Tailwind starter to style from. Base UI, Radix Primitives and Ark UI were the alternatives: each
is accessible, but none matches its translations and press handling, which the story player
(#348) needs. shadcn/ui is a reference only: copying its components would give the app the look
of many other apps, which Q16 rules out.

## Why semantic tokens only

Every colour on screen must pass contrast in both themes. A raw class that reads well on white
fails on the dark surface, and nothing notices until someone looks at that screen in dark mode.
With only semantic tokens registered, a screen names a role, the role takes a value per theme,
and an off-palette colour cannot be written at all, so no lint rule or review is needed to catch
one. The contrast test then has a finite list of pairs to check against the token stylesheet.

CSS Modules with CSS variables would have kept plain CSS, at the cost of naming and maintaining a
class per element and giving up the variants and class merging that `tailwind-variants` provides;
vanilla-extract adds a build step for the same result. Runtime CSS-in-JS was ruled out by its
cost on phones and by the CSP (`style-src 'self'`).

## Consequences

- Hard to reverse once screens exist: replacing React Aria or the token layer touches every
  component and every screen.
- A new kind of colour (a success green, say) needs a semantic token with a value for each theme
  before any screen can use it.
- React Aria's built-in strings are cut to the offered languages
  (`@react-aria/optimize-locales-plugin`); adding a language adds it there as well as to the
  catalog.
- Components keep their variant definitions in their view files, outside UI mutation (ADR 0014).
