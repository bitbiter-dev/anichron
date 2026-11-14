import { fireEvent, screen, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import { logoutSucceeds, refreshSucceeds } from '../../test/handlers/auth';
import { themeColourTags } from '../../test/index-head';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';

const perScheme = [
  '(prefers-color-scheme: light): #f7f5f2',
  '(prefers-color-scheme: dark): #171513',
];
const user = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });

async function openSettings() {
  server.use(refreshSucceeds());
  const rendered = renderApp('/settings');
  const themes = await within(rendered.container).findByRole('radiogroup', { name: 'Theme' });
  return { ...rendered, theme: (name: string) => within(themes).getByRole('radio', { name }) };
}

const root = () => document.documentElement;

describe('the theme choice', () => {
  it('offers System, Light and Dark, and starts on System following the device', async () => {
    const { theme, container } = await openSettings();

    expect(
      within(screen.getByRole('radiogroup', { name: 'Theme' }))
        .getAllByRole('radio')
        .map((radio) => radio.closest('label')?.textContent),
    ).toEqual(['System', 'Light', 'Dark']);
    expect(theme('System')).toBeChecked();
    expect(root()).not.toHaveAttribute('data-theme');
    expect(themeColourTags()).toEqual(perScheme);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('applies Dark at once, with the dark surface as the theme colour', async () => {
    const { theme, container } = await openSettings();

    fireEvent.click(theme('Dark'));

    expect(theme('Dark')).toBeChecked();
    expect(root()).toHaveAttribute('data-theme', 'dark');
    expect(themeColourTags()).toEqual(['any scheme: #171513', ...perScheme]);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('applies Light at once, with the light surface as the theme colour', async () => {
    const { theme } = await openSettings();
    fireEvent.click(theme('Dark'));

    fireEvent.click(theme('Light'));

    expect(root()).toHaveAttribute('data-theme', 'light');
    expect(themeColourTags()).toEqual(['any scheme: #f7f5f2', ...perScheme]);
  });

  it('goes back to following the device on System', async () => {
    const { theme } = await openSettings();
    fireEvent.click(theme('Dark'));

    fireEvent.click(theme('System'));

    expect(root()).not.toHaveAttribute('data-theme');
    expect(themeColourTags()).toEqual(perScheme);
  });

  it('is remembered on this device', async () => {
    const first = await openSettings();
    fireEvent.click(first.theme('Dark'));
    first.unmount();
    root().removeAttribute('data-theme');

    const { theme } = await openSettings();

    expect(theme('Dark')).toBeChecked();
    expect(root()).toHaveAttribute('data-theme', 'dark');
  });

  it('forgets a remembered choice on System', async () => {
    const first = await openSettings();
    fireEvent.click(first.theme('Light'));
    fireEvent.click(first.theme('System'));
    first.unmount();

    const { theme } = await openSettings();

    expect(theme('System')).toBeChecked();
    expect(root()).not.toHaveAttribute('data-theme');
  });

  it('follows a change made in another tab', async () => {
    const other = await openSettings();
    fireEvent.click(other.theme('Light'));
    const { theme } = await openSettings();
    fireEvent.click(other.theme('Dark'));
    expect(theme('Light')).toBeChecked();

    fireEvent(window, new StorageEvent('storage'));

    expect(theme('Dark')).toBeChecked();
    expect(root()).toHaveAttribute('data-theme', 'dark');
  });

  it('stops following other tabs once the app is closed', async () => {
    const { theme, unmount } = await openSettings();
    fireEvent.click(theme('Dark'));
    unmount();
    root().removeAttribute('data-theme');

    fireEvent(window, new StorageEvent('storage'));

    expect(root()).not.toHaveAttribute('data-theme');
  });

  it('moves between the options with the arrow keys', async () => {
    const { theme } = await openSettings();
    const keyboard = user();
    await keyboard.tab();
    expect(theme('System')).toHaveFocus();

    await keyboard.keyboard('{ArrowRight}');

    expect(theme('Light')).toBeChecked();
    expect(root()).toHaveAttribute('data-theme', 'light');
  });

  it('follows the device when storage is blocked, and still applies a choice for now', async () => {
    localStorage.setItem('anichron.theme', 'dark');
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });
    const { theme } = await openSettings();
    expect(theme('System')).toBeChecked();
    expect(root()).not.toHaveAttribute('data-theme');

    fireEvent.click(theme('Light'));

    expect(theme('Light')).toBeChecked();
    expect(root()).toHaveAttribute('data-theme', 'light');
  });
});

describe('the account section', () => {
  it('signs out at once, without asking first', async () => {
    let logouts = 0;
    server.use(logoutSucceeds(() => logouts++));
    const { router } = await openSettings();

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/login');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(logouts).toBe(1);
  });
});
