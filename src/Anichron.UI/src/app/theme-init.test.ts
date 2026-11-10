import { beforeEach, describe, expect, it, vi } from 'vitest';
import { themeColourTags } from '../test/index-head';

const perScheme = [
  '(prefers-color-scheme: light): #f7f5f2',
  '(prefers-color-scheme: dark): #171513',
];

async function start() {
  await import('./theme-init');
}

describe('the theme start-up script', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  it.each([
    ['light', '#f7f5f2'],
    ['dark', '#171513'],
  ])(
    'starts in a stored %s choice, with its surface as the theme colour',
    async (theme, colour) => {
      localStorage.setItem('anichron.theme', theme);

      await start();

      expect(document.documentElement).toHaveAttribute('data-theme', theme);
      expect(themeColourTags()).toEqual([`any scheme: ${colour}`, ...perScheme]);
    },
  );

  it('follows the device without a stored choice', async () => {
    await start();

    expect(document.documentElement).not.toHaveAttribute('data-theme');
    expect(themeColourTags()).toEqual(perScheme);
  });

  it('follows the device when storage is blocked', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });

    await start();

    expect(document.documentElement).not.toHaveAttribute('data-theme');
    expect(themeColourTags()).toEqual(perScheme);
  });
});
