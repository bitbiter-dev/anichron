import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import { adminSession, logoutSucceeds, refreshSucceeds } from '../../test/handlers/auth';
import { everyoneHasMemories } from '../../test/handlers/flashbacks';
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
    expect(screen.getByRole('link', { name: 'Change password' })).toHaveFocus();
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
  const cards = () => [...screen.getByRole('main').children];

  it('comes first, showing who is signed in with a row to change the password', async () => {
    const { container } = await openSettings();

    const account = cards()[0] as HTMLElement;
    expect(within(account).getByRole('heading', { level: 2, name: 'Account' })).toBeInTheDocument();
    expect(account).toHaveTextContent(/^AccountMmiraChange password$/);
    expect(within(account).queryByText('Admin')).toBeNull();
    const change = within(account).getByRole('link', { name: 'Change password' });
    expect(change).toHaveAttribute('href', '/settings/password');
    expect(change.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('says Admin for an admin, whose work queue is a row too', async () => {
    server.use(refreshSucceeds(adminSession));
    renderApp('/settings');

    const account = await screen.findByRole('region', { name: 'Account' });
    expect(within(account).getByText('alice')).toBeVisible();
    expect(within(account).getByText('Admin')).toBeVisible();
    const queue = screen.getByRole('link', { name: 'Work queue' });
    expect(queue).toHaveAttribute('href', '/admin/work-items');
    expect(queue.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
  });

  it.each(['light', 'dark'] as const)(
    'ends with Sign out in a card of its own, readable in the %s theme',
    async (name) => {
      const { container, theme } = await openSettings();
      fireEvent.click(theme(name === 'light' ? 'Light' : 'Dark'));

      const last = cards().at(-1) as HTMLElement;
      expect(
        within(last)
          .getAllByRole('button')
          .map((b) => b.textContent),
      ).toEqual(['Sign out']);
      expect(last).toHaveTextContent(/^Sign out$/);
      expect(await axeResults(container)).toHaveNoViolations();
    },
  );

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

describe('the install section', () => {
  const iPhone =
    'Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1';

  const pixel =
    'Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36';
  const desktop =
    'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36';

  const mac =
    'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Safari/605.1.15';

  const browsingOn = (userAgent: string, touchPoints = 0) => {
    vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue(userAgent);
    Object.defineProperty(navigator, 'maxTouchPoints', { value: touchPoints, configurable: true });
  };

  const offerPrompt = (outcome: 'accepted' | 'dismissed' = 'accepted') => {
    const event = Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
      prompt: vi.fn(() => Promise.resolve()),
      userChoice: Promise.resolve({ outcome, platform: 'web' }),
    });
    act(() => {
      window.dispatchEvent(event);
    });
    return event;
  };

  const section = () => screen.queryByRole('region', { name: 'Install the app' });

  afterEach(() => {
    Reflect.deleteProperty(navigator, 'standalone');
    Reflect.deleteProperty(navigator, 'maxTouchPoints');
  });

  it('explains Share, then Add to Home Screen, on an iPhone', async () => {
    browsingOn(iPhone);
    const { container } = await openSettings();

    expect(section()).toHaveTextContent('To install Anichron, tap Share, then Add to Home Screen.');
    expect(screen.queryByRole('button', { name: 'Install' })).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('explains the same steps on an iPad asking for the mobile site', async () => {
    browsingOn(
      'Mozilla/5.0 (iPad; CPU OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1',
    );
    await openSettings();

    expect(section()).toHaveTextContent('To install Anichron, tap Share, then Add to Home Screen.');
  });

  it('explains the same steps on an iPad, which Safari presents as a Mac', async () => {
    browsingOn(mac, 5);
    await openSettings();

    expect(section()).toHaveTextContent('To install Anichron, tap Share, then Add to Home Screen.');
  });

  it('is hidden in Safari on a Mac', async () => {
    browsingOn(mac);
    await openSettings();

    expect(section()).toBeNull();
  });

  it('is hidden in the installed app', async () => {
    browsingOn(iPhone);
    vi.spyOn(window, 'matchMedia').mockImplementation(
      (media) => ({ matches: media === '(display-mode: standalone)' }) as MediaQueryList,
    );
    const { container } = await openSettings();

    expect(section()).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('is hidden in the app installed from Safari on an iPhone', async () => {
    browsingOn(iPhone);
    Object.defineProperty(navigator, 'standalone', { value: true, configurable: true });
    await openSettings();

    expect(section()).toBeNull();
  });

  it('explains the browser menu on Android when the browser offers no prompt', async () => {
    browsingOn(pixel);
    const { container } = await openSettings();

    expect(section()).toHaveTextContent(
      'To install Anichron, open the browser menu, then tap Add to Home screen or Install app.',
    );
    expect(screen.queryByRole('button', { name: 'Install' })).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('is hidden on another browser that offers no prompt', async () => {
    browsingOn(desktop);
    const { container } = await openSettings();

    expect(section()).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('offers the browser prompt on Android, in place of the banner', async () => {
    browsingOn(pixel);
    const { container } = await openSettings();

    const prompt = offerPrompt();

    expect(prompt.defaultPrevented).toBe(true);
    expect(
      within(screen.getByRole('region', { name: 'Install the app' })).getByRole('button', {
        name: 'Install',
      }),
    ).toBeVisible();
    expect(section()).not.toHaveTextContent('browser menu');
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('fires the kept prompt on Install', async () => {
    browsingOn(pixel);
    await openSettings();
    const prompt = offerPrompt();

    fireEvent.click(screen.getByRole('button', { name: 'Install' }));

    expect(prompt.prompt).toHaveBeenCalledOnce();
  });

  it('disappears once the prompt is accepted', async () => {
    browsingOn(pixel);
    const { container } = await openSettings();
    offerPrompt('accepted');

    fireEvent.click(screen.getByRole('button', { name: 'Install' }));

    await waitFor(() => {
      expect(section()).toBeNull();
    });
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('goes back to the Android steps once the prompt is dismissed, since it fires only once', async () => {
    browsingOn(pixel);
    await openSettings();
    offerPrompt('dismissed');

    fireEvent.click(screen.getByRole('button', { name: 'Install' }));

    expect(
      await screen.findByText(
        'To install Anichron, open the browser menu, then tap Add to Home screen or Install app.',
      ),
    ).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Install' })).toBeNull();
  });

  it('keeps a prompt that came before Settings opened, on another browser', async () => {
    browsingOn(desktop);
    server.use(refreshSucceeds(), everyoneHasMemories());
    const { container, router } = renderApp('/');
    expect(await screen.findByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
    const prompt = offerPrompt();

    await act(() => router.navigate({ to: '/settings' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Install' }));

    expect(prompt.prompt).toHaveBeenCalledOnce();
    expect(section()).not.toHaveTextContent('To install Anichron');
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('asks the browser only once while its prompt is open', async () => {
    browsingOn(pixel);
    await openSettings();
    const prompt = offerPrompt('dismissed');

    fireEvent.click(screen.getByRole('button', { name: 'Install' }));
    fireEvent.click(screen.getByRole('button', { name: 'Install' }));

    expect(prompt.prompt).toHaveBeenCalledOnce();
    expect(await screen.findByText(/open the browser menu/)).toBeVisible();
  });

  it('disappears once the browser installs the app from its own menu', async () => {
    browsingOn(pixel);
    await openSettings();
    offerPrompt();

    act(() => {
      window.dispatchEvent(new Event('appinstalled'));
    });

    expect(section()).toBeNull();
  });
});
