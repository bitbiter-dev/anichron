import { expect, test } from '@playwright/test';
import { expectAccessible } from './accessibility';
import { createAccount } from './accounts';

test.skip(
  ({ browserName }) => browserName !== 'chromium',
  'The service worker runs in Chromium only',
);

const signedIn = { level: 1, name: 'On This Day' } as const;
const notice = 'A new version is available';

test('an open app offers a new build served behind the same origin, and reloads into it', async ({
  page,
  context,
  request,
  baseURL,
}) => {
  const account = await createAccount(request);
  await page.goto('/login');
  await page.getByLabel('Username or email').fill(account.username);
  await page.getByLabel('Password').fill(account.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', signedIn)).toBeVisible();
  await page.evaluate(async () => {
    await navigator.serviceWorker.ready;
  });
  await page.reload();
  await expect(page.getByRole('heading', signedIn)).toBeVisible();
  await expect(page.getByText(notice)).toBeHidden();

  await context.addCookies([{ name: 'anichron-e2e-build', value: 'next', url: baseURL ?? '' }]);
  await page.reload();

  await expect(page.getByText(notice)).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Reload' }).click();
  await expect(page.getByRole('heading', signedIn)).toBeVisible();
  await expect(page.getByText(notice)).toBeHidden();
});
