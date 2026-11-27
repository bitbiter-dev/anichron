import { expect, test } from '@playwright/test';
import { expectAccessible } from './accessibility';
import { createAccount, signInPage } from './accounts';
import { surface } from './surfaces';

const signedIn = { level: 1, name: 'On This Day' } as const;

test('the start-up theme script is served unhashed and never cached stale', async ({ request }) => {
  const response = await request.get('/theme-init.js');

  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('javascript');
  expect(response.headers()['cache-control']).toBe('no-cache');
});

test('a stored Dark choice paints dark from the first frame on a light device', async ({
  page,
  request,
}) => {
  await signInPage(page, await createAccount(request));
  await page.emulateMedia({ colorScheme: 'light' });
  await page.addInitScript(() => {
    localStorage.setItem('anichron.theme', 'dark');
    requestAnimationFrame(() => {
      Object.defineProperty(window, 'firstFrame', {
        value: {
          theme: document.documentElement.dataset.theme,
          background: getComputedStyle(document.body).backgroundColor,
        },
      });
    });
  });

  await page.reload();

  await expect(page.getByRole('heading', signedIn)).toBeVisible();
  expect(await page.evaluate(() => Reflect.get(window, 'firstFrame') as unknown)).toEqual({
    theme: 'dark',
    background: surface.dark,
  });
  await expect(page.locator('meta[name="theme-color"]').first()).toHaveAttribute(
    'content',
    '#171513',
  );
});

test('the bottom bar floats inside the screen, with items at least 44 px tall', async ({
  page,
  request,
}) => {
  await signInPage(page, await createAccount(request));
  const viewport = page.viewportSize() ?? { width: 0, height: 0 };

  const bar = await page.getByRole('navigation').boundingBox();

  expect(bar).not.toBeNull();
  expect(bar?.y ?? 0).toBeGreaterThan(viewport.height / 2);
  expect((bar?.y ?? 0) + (bar?.height ?? 0)).toBeLessThan(viewport.height);
  expect(bar?.x ?? 0).toBeGreaterThan(0);
  expect((bar?.x ?? 0) + (bar?.width ?? 0)).toBeLessThan(viewport.width);
  for (const item of await page.getByRole('navigation').getByRole('link').all())
    expect((await item.boundingBox())?.height).toBeGreaterThanOrEqual(44);
});

test('the top bar runs flat across the full width from the top edge, in the page colour at rest', async ({
  page,
  request,
}) => {
  await signInPage(page, await createAccount(request));
  await page.getByRole('link', { name: 'Settings' }).click();
  const banner = page.getByRole('banner');
  const viewport = page.viewportSize() ?? { width: 0, height: 0 };

  expect(await banner.boundingBox()).toMatchObject({ x: 0, y: 0, width: viewport.width });
  for (const scheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme: scheme });
    await expect(banner).toHaveCSS('background-color', surface[scheme]);
    await expect(banner).toHaveCSS('border-bottom-left-radius', '0px');
  }
});

test('choosing a theme in Settings applies it and sets the theme colour', async ({
  page,
  request,
}) => {
  await signInPage(page, await createAccount(request));
  await page.emulateMedia({ colorScheme: 'light' });
  await page.getByRole('link', { name: 'Settings' }).click();
  const body = page.locator('body');
  const background = () => body.evaluate((element) => getComputedStyle(element).backgroundColor);

  await page.locator('label', { hasText: 'Dark' }).click();
  await expect.poll(background).toBe(surface.dark);
  await expect(page.locator('meta[name="theme-color"]').first()).toHaveAttribute(
    'content',
    '#171513',
  );

  await page.locator('label', { hasText: 'System' }).click();
  await expect.poll(background).toBe(surface.light);
  await expect(page.locator('meta[name="theme-color"]:not([media])')).toHaveCount(0);
  await expectAccessible(page);
});

for (const colorScheme of ['light', 'dark'] as const) {
  test(`Home and Settings are captured in ${colorScheme}`, async ({ page, request }, testInfo) => {
    await page.emulateMedia({ colorScheme });
    await signInPage(page, await createAccount(request));
    await expectAccessible(page);
    await page.emulateMedia({ colorScheme });
    await page.screenshot({
      path: testInfo.outputPath(`home-${colorScheme}.png`),
      animations: 'disabled',
    });

    await page.getByRole('link', { name: 'Settings' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    await expectAccessible(page);
    await page.emulateMedia({ colorScheme });
    await page.screenshot({
      path: testInfo.outputPath(`settings-${colorScheme}.png`),
      animations: 'disabled',
    });
  });
}
