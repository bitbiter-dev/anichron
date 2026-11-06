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

export type Theme = 'light' | 'dark';
export type ThemeColours = Record<Theme, Record<string, string>>;

// Reads the semantic colours (`--color-<name>`) of a token stylesheet, through its
// `light-dark()` and `var()` references, down to their `#rrggbb` values.
type Groups<T extends string[]> = [string, ...T];

export function themeColours(css: string): ThemeColours {
  const matches = css.matchAll(/--([\w-]+):\s*([^;]+);/g) as Iterable<Groups<[string, string]>>;
  const declared = new Map([...matches].map(([, name, value]) => [name, value]));
  const resolve = (value: string, theme: Theme): string => {
    const lightDark = /^light-dark\((.+?),\s*(.+)\)$/.exec(value) as Groups<
      [string, string]
    > | null;
    if (lightDark) return resolve({ light: lightDark[1], dark: lightDark[2] }[theme], theme);
    const reference = /^var\(--([\w-]+)\)$/.exec(value) as Groups<[string]> | null;
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
