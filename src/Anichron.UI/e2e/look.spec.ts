import { expect, test, type Locator, type Page } from '@playwright/test';
import { surface } from './surfaces';

const accent = 'rgb(15, 118, 110)';

const controls = (page: Page) => [
  page.getByLabel('Username or email'),
  page.getByLabel('Password'),
  page.getByRole('button', { name: 'Sign in' }),
];

const style = (locator: Locator, property: string) =>
  locator.evaluate((element, name) => getComputedStyle(element).getPropertyValue(name), property);

const topRowColours = async (page: Page) => {
  const width = page.viewportSize()?.width ?? 0;
  const png = await page.screenshot({ clip: { x: 0, y: 0, width, height: 1 } });
  return page.evaluate(async (base64) => {
    const bytes = Uint8Array.from(atob(base64), (character) => character.charCodeAt(0));
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    const context = canvas.getContext('2d');
    context?.drawImage(bitmap, 0, 0);
    const pixels = context?.getImageData(0, 0, bitmap.width, bitmap.height).data ?? [];
    const colours = new Set<string>();
    for (let index = 0; index < pixels.length; index += 4)
      colours.add(`rgb(${Array.from(pixels.slice(index, index + 3)).join(', ')})`);
    return [...colours];
  }, png.toString('base64'));
};

const ring = (locator: Locator) =>
  locator.evaluate((element) => {
    const { outlineStyle, outlineWidth, outlineOffset, outlineColor } = getComputedStyle(element);
    return [outlineStyle, outlineWidth, outlineOffset, outlineColor].join(' ');
  });

test('the login screen follows the system colour scheme at once', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('/login');
  const body = page.locator('body');

  await expect.poll(() => style(body, 'background-color')).toBe(surface.light);
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect.poll(() => style(body, 'background-color')).toBe(surface.dark);
});

test('the login screen stops its motion when the user asks for less', async ({ page }) => {
  await page.goto('/login');
  const submit = page.getByRole('button', { name: 'Sign in' });

  expect(await style(submit, 'transition-duration')).toBe('0.15s');
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await expect.poll(() => style(submit, 'transition-duration')).toBe('0s');
});

test('keyboard focus shows the accent ring, and a press shows none', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  for (const control of controls(page)) {
    await page.keyboard.press('Tab');
    await expect(control).toBeFocused();
    expect(await ring(control)).toBe(`solid 2px 2px ${accent}`);
  }

  await page.getByRole('heading', { level: 1 }).click();
  const submit = page.getByRole('button', { name: 'Sign in' });
  await submit.hover();
  await page.mouse.down();
  expect(await ring(submit)).toMatch(/^none /);
  await page.mouse.up();
});

test('every login control is at least 44 px tall', async ({ page }) => {
  await page.goto('/login');

  for (const control of controls(page)) {
    expect((await control.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  }
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`the login screen's top edge is the ${colorScheme} surface, so it meets the status bar without a line`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme });
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    expect(await topRowColours(page)).toEqual([surface[colorScheme]]);
  });

  test(`the login screen is captured in ${colorScheme}`, async ({ page }, testInfo) => {
    await page.emulateMedia({ colorScheme });
    await page.goto('/login');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await page.screenshot({
      path: testInfo.outputPath(`login-${colorScheme}.png`),
      animations: 'disabled',
    });
  });
}
