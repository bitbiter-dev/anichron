import { expect, test, type APIRequestContext } from '@playwright/test';

const pngSize = (png: Buffer) => [png.readUInt32BE(16), png.readUInt32BE(20)].join('x');

const fetchPngSize = async (request: APIRequestContext, path: string) => {
  const response = await request.get(path);
  expect(response.status(), path).toBe(200);
  expect(response.headers()['content-type'], path).toBe('image/png');
  return pngSize(await response.body());
};

test('the app links the head tags that put it on the Home Screen', async ({ page }) => {
  await page.goto('/login');
  const head = page.locator('head');

  await expect(head.locator('link[rel="manifest"]')).toHaveAttribute(
    'href',
    '/manifest.webmanifest',
  );
  await expect(head.locator('link[rel="apple-touch-icon"]')).toHaveAttribute(
    'href',
    '/apple-touch-icon.png',
  );
  await expect(
    head.locator('meta[name="theme-color"][media="(prefers-color-scheme: light)"]'),
  ).toHaveAttribute('content', '#f7f5f2');
  await expect(
    head.locator('meta[name="theme-color"][media="(prefers-color-scheme: dark)"]'),
  ).toHaveAttribute('content', '#171513');
});

test('the manifest names the app, its colours and icons that exist at their sizes', async ({
  request,
}) => {
  const response = await request.get('/manifest.webmanifest');

  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toBe('application/manifest+json');
  const manifest = (await response.json()) as { icons: { src: string; sizes: string }[] };
  expect(manifest).toEqual({
    name: 'Anichron',
    short_name: 'Anichron',
    display: 'standalone',
    start_url: '/',
    scope: '/',
    background_color: '#171513',
    theme_color: '#171513',
    icons: [
      { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
      { src: '/icon-512.png', sizes: '512x512', type: 'image/png' },
      { src: '/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],
  });
  for (const icon of manifest.icons)
    expect(await fetchPngSize(request, icon.src), icon.src).toBe(icon.sizes);
  expect(await fetchPngSize(request, '/apple-touch-icon.png')).toBe('180x180');
});
