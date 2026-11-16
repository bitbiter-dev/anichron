import { expect, test } from '@playwright/test';

test.skip(
  ({ browserName }) => browserName !== 'chromium',
  'The service worker runs in Chromium only',
);

test('after a first visit, a reload with no network starts the frame and says it cannot reach Anichron', async ({
  page,
  context,
}) => {
  await page.goto('/login');
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Anichron' })).toBeVisible();
  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });

  await context.setOffline(true);
  await page.reload();

  await expect(page.getByRole('heading', { level: 1, name: "Can't reach Anichron" })).toBeVisible();
});
