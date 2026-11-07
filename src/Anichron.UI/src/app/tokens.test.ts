import { describe, expect, it } from 'vitest';
import { blend, contrastRatio, declarations, themeColours, type Theme } from '../design/contrast';
import tokens from './tokens.css?raw';

const colours = themeColours(tokens);
const glassOpacity = Number(declarations(tokens).get('glass-opacity'));
const surfaces = ['surface', 'surface-raised'];

const text = [
  ...surfaces.flatMap((surface) => [
    ['fg', surface],
    ['fg-muted', surface],
    ['danger', surface],
  ]),
  ['on-accent', 'accent'],
];
const nonText = surfaces.flatMap((surface) => [
  ['focus', surface],
  ['border', surface],
]);

it('makes the glass card see-through, but mostly card', () => {
  expect(glassOpacity).toBeGreaterThan(0.5);
  expect(glassOpacity).toBeLessThan(1);
});

describe.each<Theme>(['light', 'dark'])('the %s theme', (theme) => {
  const ratio = (foreground: string, background: string) =>
    contrastRatio(colours[theme][foreground] ?? '', colours[theme][background] ?? '');

  it('has exactly the semantic colours', () => {
    expect(Object.keys(colours[theme]).sort()).toEqual([
      'accent',
      'backdrop-accent',
      'backdrop-cool',
      'backdrop-warm',
      'border',
      'danger',
      'fg',
      'fg-muted',
      'focus',
      'on-accent',
      'player',
      'surface',
      'surface-raised',
    ]);
  });

  it.each(text)('reads %s on %s at 4.5:1 or more', (foreground, background) => {
    expect(ratio(foreground, background)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(nonText)('shows %s against %s at 3:1 or more', (foreground, background) => {
    expect(ratio(foreground, background)).toBeGreaterThanOrEqual(3);
  });

  describe.each(['surface', 'backdrop-accent', 'backdrop-cool', 'backdrop-warm'])(
    'on the glass card over %s',
    (backdrop) => {
      const glass = () =>
        blend(colours[theme]['surface-raised'] ?? '', colours[theme][backdrop] ?? '', glassOpacity);
      const onGlass = (foreground: string) =>
        contrastRatio(colours[theme][foreground] ?? '', glass());

      it.each(['fg', 'fg-muted', 'danger'])('reads %s at 4.5:1 or more', (foreground) => {
        expect(onGlass(foreground)).toBeGreaterThanOrEqual(4.5);
      });

      it.each(['focus', 'border'])('shows %s at 3:1 or more', (foreground) => {
        expect(onGlass(foreground)).toBeGreaterThanOrEqual(3);
      });
    },
  );

  it('keeps the player black', () => {
    expect(colours[theme].player).toBe('#000000');
  });
});
