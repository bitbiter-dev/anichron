import { expect, test } from '@playwright/test';
import { expectAccessible } from './accessibility';
import { createAccount, fillInSignIn } from './accounts';

const message = 'No photos are set up for you yet.';

test('Today says no photos are set up, to each user without them', async ({ page, request }) => {
  const first = await createAccount(request, { photos: false });
  const second = await createAccount(request, { photos: false });

  await page.goto('/login');
  await fillInSignIn(page, first);
  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
  await expect(page.getByText(message)).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expectAccessible(page);

  await page.getByRole('link', { name: 'Settings' }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL('/login');
  await fillInSignIn(page, second);

  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
  await expect(page.getByText(message)).toBeVisible();
});
