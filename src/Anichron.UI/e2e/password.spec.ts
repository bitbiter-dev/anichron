import { expect, test, type Page } from '@playwright/test';
import { expectAccessible } from './accessibility';
import { createAccount, fillInSignIn } from './accounts';

const toast = 'Password changed.';

async function submitPasswordChange(page: Page, current: string, next: string) {
  await page.getByLabel('Current password').fill(current);
  await page.getByLabel('New password').fill(next);
  await page.getByLabel('Confirm new password').fill(next);
  await page.getByRole('button', { name: 'Change password' }).click();
}

test('a temporary password is changed first, then the requested page opens', async ({
  page,
  request,
}) => {
  const account = await createAccount(request, { keepTemporaryPassword: true });

  await page.goto('/settings');
  await expect(page).toHaveURL('/login?redirect=%2Fsettings');
  await fillInSignIn(page, account);

  await expect(
    page.getByRole('heading', { level: 1, name: 'Choose a new password' }),
  ).toBeVisible();
  await expect(page).toHaveURL('/change-password?redirect=%2Fsettings');
  await expectAccessible(page);
  await submitPasswordChange(page, account.password, `${account.username} own passphrase`);

  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  await expect(page.getByText(toast)).toBeVisible();
  await expect(page).toHaveURL('/settings');
});

test('a password changed from settings goes home with a toast', async ({ page, request }) => {
  const account = await createAccount(request);
  await page.goto('/login');
  await fillInSignIn(page, account);
  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();

  await page.getByRole('link', { name: 'Settings' }).click();
  await page.getByRole('link', { name: 'Change password' }).click();
  await submitPasswordChange(page, account.password, `${account.username} new passphrase`);

  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
  await expect(page.getByText(toast)).toBeVisible();
  await expect(page).toHaveURL('/');
  await page.goBack();
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
});
