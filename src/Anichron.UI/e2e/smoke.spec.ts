import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

const appPolicy =
  "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; " +
  "media-src 'self' blob:; connect-src 'self'; worker-src 'self'; manifest-src 'self'; " +
  "font-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; " +
  "frame-ancestors 'none'; require-trusted-types-for 'script'";

test('the placeholder loads with its headers and requests nothing but its own origin', async ({
  page,
  context,
  baseURL,
}) => {
  const origin = new URL(baseURL ?? '').origin;
  const requested: string[] = [];
  const assetCaching: (string | undefined)[] = [];
  const pageErrors: string[] = [];
  context.on('request', (request) => requested.push(request.url()));
  context.on('response', (response) => {
    if (new URL(response.url()).pathname.startsWith('/assets/'))
      assetCaching.push(response.headers()['cache-control']);
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));
  await page.addInitScript(() => {
    const violations: string[] = [];
    Object.defineProperty(window, 'cspViolations', { value: violations });
    document.addEventListener('securitypolicyviolation', (event) => {
      violations.push(`${event.effectiveDirective} ${event.blockedURI}`);
    });
  });

  const response = await page.goto('/');

  await expect(page.getByRole('heading', { level: 1, name: 'Anichron' })).toBeVisible();
  await expect(page.getByText('Your photo library is on its way.')).toBeVisible();
  const headers = response?.headers() ?? {};
  expect(headers['content-security-policy']).toBe(appPolicy);
  expect(headers['referrer-policy']).toBe('same-origin');
  expect(headers['x-content-type-options']).toBe('nosniff');
  expect(headers['permissions-policy']).toBe(
    'accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()',
  );
  expect(headers['cache-control']).toBe('no-cache');
  expect(assetCaching).not.toHaveLength(0);
  expect(assetCaching).toEqual(assetCaching.map(() => 'public, max-age=31536000, immutable'));
  expect(await page.evaluate(() => Reflect.get(window, 'cspViolations') as string[])).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(requested.filter((url) => new URL(url).origin !== origin)).toEqual([]);

  const accessibility = await new AxeBuilder({ page }).analyze();
  expect(accessibility.violations).toEqual([]);
});
