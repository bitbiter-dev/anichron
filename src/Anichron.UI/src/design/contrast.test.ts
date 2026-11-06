import { describe, expect, it } from 'vitest';
import { contrastRatio, themeColours } from './contrast';

describe('contrastRatio', () => {
  it('is 21 for black on white, in either order', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 2);
    expect(contrastRatio('#ffffff', '#000000')).toBeCloseTo(21, 2);
  });

  it.each([
    ['#ff0000', 5.25],
    ['#00ff00', 15.3],
    ['#0000ff', 2.44],
  ])('weighs %s against black as WebAIM does', (colour, ratio) => {
    expect(contrastRatio(colour, '#000000')).toBeCloseTo(ratio, 1);
  });

  it('matches WebAIM for a mid grey on white', () => {
    expect(contrastRatio('#777777', '#ffffff')).toBeCloseTo(4.48, 2);
  });

  it('takes a channel of 10 or less as linear', () => {
    expect(contrastRatio('#0a0a0a', '#000000')).toBeCloseTo(1.060705, 5);
  });
});

describe('themeColours', () => {
  const css = `
    :root {
      --grey-1:#111111;
      --grey-9: #999999;
    }
    @theme {
      --color-*: initial;
      --color-ink: light-dark(var(--grey-1),var(--grey-9));
      --color-ring: var(--color-ink);
      --color-night: #000000;
      --radius-card: 1rem;
    }
  `;

  it('resolves each semantic colour for each theme', () => {
    expect(themeColours(css)).toEqual({
      light: { ink: '#111111', ring: '#111111', night: '#000000' },
      dark: { ink: '#999999', ring: '#999999', night: '#000000' },
    });
  });

  it('fails on a variable the stylesheet does not declare', () => {
    expect(() => themeColours('@theme { --color-ink: var(--missing); }')).toThrow(
      '--missing is not declared',
    );
  });

  it.each([
    'oklch(0.5 0.1 40)',
    'x light-dark(#000000, #ffffff)',
    'light-dark(#000000, #ffffff) x',
    'x var(--color-ink)',
    'var(--color-ink) x',
    'x#111111',
    '#1111111',
  ])('fails on %s, a colour it cannot read', (value) => {
    expect(() => themeColours(`@theme { --color-ink: ${value}; }`)).toThrow(
      `${value} is not a #rrggbb colour`,
    );
  });
});
