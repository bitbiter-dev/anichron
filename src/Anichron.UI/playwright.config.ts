import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'https://localhost:8443',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'webkit-iphone', use: { ...devices['iPhone 16'] } },
    { name: 'chromium-pixel', use: { ...devices['Pixel 9'] } },
    {
      name: 'firefox-phone',
      use: { browserName: 'firefox', viewport: { width: 393, height: 852 } },
    },
  ],
});
