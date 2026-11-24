import { expect, test, type Locator, type Page } from '@playwright/test';
import { expectAccessible } from './accessibility';
import { mediaUser } from './accounts';

const today = new Date('2026-06-15T09:00:00Z');
const memoryMilliseconds = 5000;
const holdMilliseconds = 600;

test.use({ timezoneId: 'UTC' });

const story = (page: Page, day = 'June 15, 2026') =>
  page.getByRole('dialog', { name: `Memories of ${day}` });
const memory = (page: Page, name: string) => story(page).getByRole('img', { name, exact: true });
const button = (page: Page, name: string) => story(page).getByRole('button', { name, exact: true });

async function signIn(page: Page) {
  await page.clock.install({ time: today });
  await page.goto('/login');
  await page.getByLabel('Username or email').fill(mediaUser.username);
  await page.getByLabel('Password').fill(mediaUser.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
}

async function open(page: Page, group: string) {
  await signIn(page);
  await page.getByRole('link', { name: group }).click();
  await expect(story(page)).toBeVisible();
}

async function expectShown(shown: Locator) {
  await expect(shown).toBeVisible();
  await expect
    .poll(() =>
      shown
        .locator('img')
        .evaluate((image: HTMLImageElement) => image.complete && image.naturalWidth > 0),
    )
    .toBe(true);
}

async function freezeClock(page: Page) {
  await page.clock.pauseAt((await page.evaluate(() => Date.now())) + 500);
}

async function swipe(page: Page, dx: number, dy: number) {
  const { width, height } = page.viewportSize() ?? { width: 0, height: 0 };
  await page.mouse.move(width / 2, height / 2);
  await page.mouse.down();
  await page.mouse.move(width / 2 + dx, height / 2 + dy, { steps: 12 });
  await page.mouse.up();
}

const touch = { pointerType: 'touch', pointerId: 7, isPrimary: true, button: 0, buttons: 1 };

test('the landing screen offers both year groups and a recent day', async ({ page }) => {
  await signIn(page);

  await expect(page.getByRole('link', { name: '1 year ago · 2025' })).toBeVisible();
  await expect(page.getByRole('link', { name: '2 years ago · 2024' })).toBeVisible();
  const recent = page.getByRole('list', { name: 'Recent days' });
  await expect(recent.getByRole('link', { name: 'Sat 13 Jun' })).toBeVisible();
  await expectAccessible(page);

  await recent.getByRole('link', { name: 'Sat 13 Jun' }).click();

  await expectShown(story(page, 'June 13, 2026').getByRole('img', { name: 'Photo, 2025, 1 of 1' }));
});

test('the story advances on its own, a burst shows as its cover, and taps skip', async ({
  page,
}) => {
  await open(page, '1 year ago · 2025');

  await expect(story(page).getByRole('status')).toHaveText('1 year ago · 2025');
  await expectShown(memory(page, 'Photo, 2025, 1 of 2'));
  await freezeClock(page);
  await page.clock.runFor(memoryMilliseconds);

  await expectShown(memory(page, 'Burst, 2025, 2 of 2'));
  await expect(story(page).getByText('+2', { exact: true })).toBeVisible();

  await button(page, 'Previous').click();
  await expectShown(memory(page, 'Photo, 2025, 1 of 2'));

  await button(page, 'Next').click();
  await expectShown(memory(page, 'Burst, 2025, 2 of 2'));

  await button(page, 'Next').click();
  await expect(story(page).getByRole('status')).toHaveText('2 years ago · 2024');
  await expectShown(memory(page, 'Live Photo, 2024, 1 of 2'));
});

test('a hold pauses the story without the system menu', async ({ page }) => {
  await open(page, '1 year ago · 2025');
  await expectShown(memory(page, 'Photo, 2025, 1 of 2'));
  await freezeClock(page);
  const zone = button(page, 'Next');

  await zone.dispatchEvent('pointerdown', touch);
  await page.clock.runFor(holdMilliseconds);

  await expect(button(page, 'Pause')).toBeHidden();
  const menuPrevented = await zone.evaluate(
    (element) =>
      !element.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true })),
  );
  expect(menuPrevented).toBe(true);
  await page.clock.runFor(memoryMilliseconds * 2);
  await expect(memory(page, 'Photo, 2025, 1 of 2')).toBeVisible();

  await page.evaluate((init) => window.dispatchEvent(new PointerEvent('pointerup', init)), touch);

  await expect(button(page, 'Pause')).toBeVisible();
});

test('a Live Photo plays its video and a video starts muted until the speaker is tapped', async ({
  page,
}) => {
  await open(page, '2 years ago · 2024');
  await expectShown(memory(page, 'Live Photo, 2024, 1 of 2'));
  await freezeClock(page);
  const live = button(page, 'Play Live Photo');

  await live.click();

  await expect(live).toHaveAttribute('data-live', 'playing');
  await expect(live).toHaveAttribute('data-live', 'still');

  await button(page, 'Next').click();
  await expect(story(page).getByRole('img', { name: 'Video, 2024, 2 of 2' })).toBeAttached();
  const video = story(page).locator('video').first();
  await expect(video).toHaveJSProperty('paused', false);
  await expect(video).toHaveJSProperty('muted', true);

  await button(page, 'Turn sound on').click();

  await expect(button(page, 'Turn sound off')).toBeVisible();
  await expect(video).toHaveJSProperty('muted', false);
});

test('a swipe changes year group and a swipe down closes the story', async ({ page }) => {
  await open(page, '1 year ago · 2025');
  await expectShown(memory(page, 'Photo, 2025, 1 of 2'));
  await button(page, 'Pause').click();
  await expectAccessible(page, { settled: false });

  await swipe(page, -150, 0);
  await expectShown(memory(page, 'Live Photo, 2024, 1 of 2'));

  await swipe(page, 150, 0);
  await expectShown(memory(page, 'Photo, 2025, 1 of 2'));

  const { height } = page.viewportSize() ?? { height: 0 };
  await swipe(page, 0, height / 2);

  await expect(story(page)).toBeHidden();
  await expect(page.getByRole('link', { name: '1 year ago · 2025' })).toBeVisible();
});
