import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { createAccount, disable, lockOut } from './accounts';

const signedIn = "You're signed in. Your photo library is on its way.";
const pageErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  pageErrors.length = 0;
  page.on('pageerror', (error) => pageErrors.push(error.message));
  await page.addInitScript(() => {
    const violations: string[] = [];
    Object.defineProperty(window, 'cspViolations', { value: violations });
    document.addEventListener('securitypolicyviolation', (event) => {
      violations.push(`${event.effectiveDirective} ${event.blockedURI}`);
    });
  });
});

test.afterEach(async ({ page }) => {
  expect(await page.evaluate(() => Reflect.get(window, 'cspViolations') as string[])).toEqual([]);
  expect(pageErrors).toEqual([]);
});

async function signIn(page: Page, usernameOrEmail: string, password: string) {
  await page.goto('/login');
  await page.getByLabel('Username or email').fill(usernameOrEmail);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

async function expectAccessible(page: Page) {
  const accessibility = await new AxeBuilder({ page }).analyze();
  expect(accessibility.violations).toEqual([]);
}

test('a user signs in and lands on the signed-in page', async ({ page, request }) => {
  const account = await createAccount(request);

  await signIn(page, account.username, account.password);

  await expect(page.getByText(signedIn)).toBeVisible();
  await expect(page).toHaveURL('/');
  await expectAccessible(page);
});

test('a wrong password is refused in the user’s language', async ({ page, request }) => {
  const account = await createAccount(request);

  await signIn(page, account.username, 'not the password');

  await expect(page.getByRole('alert')).toHaveText('The username, email or password is wrong.');
  await expect(page).toHaveURL('/login');
  await expectAccessible(page);
});

test('a disabled account is told to ask the administrator', async ({ page, request }) => {
  const account = await createAccount(request);
  await disable(request, account);

  await signIn(page, account.username, account.password);

  await expect(page.getByRole('alert')).toHaveText(
    'This account is disabled. Ask your administrator to enable it.',
  );
  await expect(page).toHaveURL('/login');
});

test('a locked account counts down, then signs in', async ({ page, request }) => {
  test.slow();
  const account = await createAccount(request);
  await lockOut(request, account);

  await signIn(page, account.username, account.password);

  const alert = page.getByRole('alert');
  await expect(alert).toContainText('Too many failed attempts. This account is locked for now.');
  await expect(alert).toContainText(/You can try again in \d+ seconds?\./);
  const submit = page.getByRole('button', { name: 'Sign in' });
  await expect(submit).toBeDisabled();
  await expectAccessible(page);

  await expect(submit).toBeEnabled({ timeout: 10_000 });
  await expect(alert).not.toContainText('You can try again');
  await submit.click();

  await expect(page.getByText(signedIn)).toBeVisible();
});
