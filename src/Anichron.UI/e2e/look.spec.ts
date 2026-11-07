import { expect, test, type Locator, type Page } from '@playwright/test';

const surface = { light: 'rgb(247, 245, 242)', dark: 'rgb(23, 21, 19)' };
const accent = 'rgb(15, 118, 110)';

const controls = (page: Page) => [
  page.getByLabel('Username or email'),
  page.getByLabel('Password'),
  page.getByRole('button', { name: 'Sign in' }),
];

const style = (locator: Locator, property: string) =>
  locator.evaluate((element, name) => getComputedStyle(element).getPropertyValue(name), property);

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
