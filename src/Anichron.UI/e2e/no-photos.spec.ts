import { expect, test, type Page } from '@playwright/test';
import { expectAccessible } from './accessibility';
import { createAccount, type Account } from './accounts';

const message = 'No photos are set up for you yet.';

async function fillIn(page: Page, account: Account) {
  await page.getByLabel('Username or email').fill(account.username);
  await page.getByLabel('Password').fill(account.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test('Today says no photos are set up, to each user without them', async ({ page, request }) => {
  const first = await createAccount(request, { photos: false });
  const second = await createAccount(request, { photos: false });

  await page.goto('/login');
  await fillIn(page, first);
  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
  await expect(page.getByText(message)).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expectAccessible(page);

  await page.getByRole('link', { name: 'Settings' }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL('/login');
  await fillIn(page, second);

  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
  await expect(page.getByText(message)).toBeVisible();
});
