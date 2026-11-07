import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';

export async function expectAccessible(page: Page) {
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme, reducedMotion: 'reduce' });
    const accessibility = await new AxeBuilder({ page }).analyze();
    expect(accessibility.violations, colorScheme).toEqual([]);
  }
  await page.emulateMedia({ colorScheme: null, reducedMotion: null });
}
