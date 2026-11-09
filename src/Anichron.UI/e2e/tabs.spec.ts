import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { expectAccessible } from './accessibility';
import { createAccount } from './accounts';

const signedIn = "You're signed in. Your photo library is on its way.";
const pageErrors: string[] = [];
const pages: Page[] = [];

async function openTab(context: BrowserContext) {
  const page = await context.newPage();
  page.on('pageerror', (error) => pageErrors.push(error.message));
  await page.addInitScript(() => {
    const violations: string[] = [];
    Object.defineProperty(window, 'cspViolations', { value: violations });
    document.addEventListener('securitypolicyviolation', (event) => {
      violations.push(`${event.effectiveDirective} ${event.blockedURI}`);
    });
  });
  pages.push(page);
  return page;
}

test.beforeEach(() => {
  pageErrors.length = 0;
  pages.length = 0;
});

test.afterEach(async () => {
  for (const page of pages)
    expect(await page.evaluate(() => Reflect.get(window, 'cspViolations') as string[])).toEqual([]);
  expect(pageErrors).toEqual([]);
});

test('two tabs reload together and both stay signed in, until one signs out', async ({
  context,
  request,
}) => {
  const account = await createAccount(request);
  const first = await openTab(context);
  const second = await openTab(context);

  await first.goto('/login');
  await first.getByLabel('Username or email').fill(account.username);
  await first.getByLabel('Password').fill(account.password);
  await first.getByRole('button', { name: 'Sign in' }).click();
  await expect(first.getByText(signedIn)).toBeVisible();
  await second.goto('/');
  await expect(second.getByText(signedIn)).toBeVisible();

  await Promise.all([first.reload(), second.reload()]);
  await expect(first.getByText(signedIn)).toBeVisible();
  await expect(second.getByText(signedIn)).toBeVisible();

  await Promise.all([first.reload(), second.reload()]);
  await expect(first.getByText(signedIn)).toBeVisible();
  await expect(second.getByText(signedIn)).toBeVisible();
  await expectAccessible(second);

  await first.getByRole('button', { name: 'Sign out' }).click();
  const loginHeading = { level: 1, name: 'Sign in to Anichron' } as const;
  await expect(first.getByRole('heading', loginHeading)).toBeVisible();
  await expect(second.getByRole('heading', loginHeading)).toBeVisible();
  await second.reload();
  await expect(second.getByRole('heading', loginHeading)).toBeVisible();
});
