import { describe, expect, it } from 'vitest';
import { focusVariant, tv } from './tv';

const merge = (base: string, override: string) => tv({ base })({ class: override });

describe('tv', () => {
  it('keeps a type size next to a text colour', () => {
    expect(merge('text-small', 'text-danger')).toBe('text-small text-danger');
  });

  it.each([
    ['text-small', 'text-title'],
    ['text-title', 'text-body'],
    ['text-body', 'text-small'],
    ['rounded-field', 'rounded-card'],
    ['rounded-card', 'rounded-field'],
  ])('lets a caller’s %s give way to %s', (base, override) => {
    expect(merge(base, override)).toBe(override);
  });

  it('lets a caller’s colour override the default', () => {
    expect(merge('bg-accent text-on-accent', 'bg-surface')).toBe('text-on-accent bg-surface');
  });

  it.each([
    [true, 'focus-ring'],
    [false, 'outline-none'],
  ])('draws the focus ring only on keyboard focus (%s)', (isFocusVisible, classes) => {
    expect(tv({ variants: focusVariant() })({ isFocusVisible })).toBe(classes);
  });

  it('adds a control’s own classes to the focus ring', () => {
    expect(tv({ variants: focusVariant('transition-none') })({ isFocusVisible: true })).toBe(
      'focus-ring transition-none',
    );
  });
});
