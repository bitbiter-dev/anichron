# The UI is built on React Aria Components and Tailwind semantic tokens

Status: accepted (decided on #346)

## Decision

Every screen is built from React Aria Components, styled with Tailwind CSS v4 through `tailwind-variants`, and coloured only through **semantic tokens**. The raw scales are CSS variables that only the semantic tokens read. Tailwind's default palette is cleared, so `bg-blue-500` generates nothing. Radius and type size follow the same split. Details: `docs/architecture/ui-design-system.md`.

## Why

- **React Aria:** the most complete accessibility of the headless libraries (roles, focus, press handling that behaves the same on touch, mouse and keyboard), with translated strings, German included. The story player needs its press handling.
- **Semantic tokens only:** every colour must pass contrast in both themes. A screen names a role, the role has a value per theme, and an off-palette colour cannot be written, so the contrast test has a finite list of pairs.

## Rejected

- **Base UI, Radix, Ark UI:** accessible, but weaker on translations and press handling.
- **shadcn/ui:** a reference only; copying it gives the look of many other apps.
- **CSS Modules or vanilla-extract:** a class per element, or an extra build step, without variants and class merging.
- **Runtime CSS-in-JS:** its cost on phones, and it breaks the CSP (`style-src 'self'`).

## Consequences

- Hard to reverse once screens exist.
- A new kind of colour needs a semantic token with a value for each theme first.
- A new language is added to `@react-aria/optimize-locales-plugin` as well as the catalog.
- Variant definitions stay in view files, outside UI mutation (ADR 0014).
