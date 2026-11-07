// WCAG 2.2 relative luminance and contrast ratio, for `#rrggbb` colours.
function channel(hex: string, offset: number) {
  const byte = Number.parseInt(hex.slice(offset, offset + 2), 16);
  const value = byte / 255;
  return byte <= 10 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
}

function luminance(hex: string) {
  return 0.2126 * channel(hex, 1) + 0.7152 * channel(hex, 3) + 0.0722 * channel(hex, 5);
}

export function contrastRatio(a: string, b: string) {
  const [lighter, darker] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (lighter + 0.05) / (darker + 0.05);
}

// `top` at `opacity` composited over `bottom`.
export function blend(top: string, bottom: string, opacity: number) {
  const mixed = [1, 3, 5].map((offset) => {
    const byte = (hex: string) => Number.parseInt(hex.slice(offset, offset + 2), 16);
    return Math.round(byte(top) * opacity + byte(bottom) * (1 - opacity));
  });
  return `#${mixed.map((value) => value.toString(16).padStart(2, '0')).join('')}`;
}

export type Theme = 'light' | 'dark';
export type ThemeColours = Record<Theme, Record<string, string>>;

type RegexMatch<T extends string[]> = [string, ...T];

export function declarations(css: string) {
  const matches = css.matchAll(/--([\w-]+):\s*([^;]+);/g) as Iterable<RegexMatch<[string, string]>>;
  return new Map([...matches].map(([, name, value]) => [name, value]));
}

// Reads the semantic colours (`--color-<name>`) of a token stylesheet, through its
// `light-dark()` and `var()` references, down to their `#rrggbb` values.
export function themeColours(css: string): ThemeColours {
  const declared = declarations(css);
  const resolve = (value: string, theme: Theme): string => {
    const lightDark = /^light-dark\((.+?),\s*(.+)\)$/.exec(value) as RegexMatch<
      [string, string]
    > | null;
    if (lightDark) return resolve({ light: lightDark[1], dark: lightDark[2] }[theme], theme);
    const reference = /^var\(--([\w-]+)\)$/.exec(value) as RegexMatch<[string]> | null;
    if (reference) {
      const target = declared.get(reference[1]);
      if (target === undefined) throw new Error(`--${reference[1]} is not declared`);
      return resolve(target, theme);
    }
    if (!/^#[0-9a-f]{6}$/.test(value)) throw new Error(`${value} is not a #rrggbb colour`);
    return value;
  };
  const forTheme = (theme: Theme) =>
    Object.fromEntries(
      [...declared]
        .filter(([name]) => name.startsWith('color-'))
        .map(([name, value]) => [name.slice('color-'.length), resolve(value, theme)]),
    );
  return { light: forTheme('light'), dark: forTheme('dark') };
}
