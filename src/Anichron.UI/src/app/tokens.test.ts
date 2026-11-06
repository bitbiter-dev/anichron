import { describe, expect, it } from 'vitest';
import { contrastRatio, themeColours, type Theme } from '../design/contrast';
import tokens from './tokens.css?raw';

const colours = themeColours(tokens);
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

describe.each<Theme>(['light', 'dark'])('the %s theme', (theme) => {
  const ratio = (foreground: string, background: string) =>
    contrastRatio(colours[theme][foreground] ?? '', colours[theme][background] ?? '');

  it('has exactly the semantic colours', () => {
    expect(Object.keys(colours[theme]).sort()).toEqual([
      'accent',
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

  it('keeps the player black', () => {
    expect(colours[theme].player).toBe('#000000');
  });
});
