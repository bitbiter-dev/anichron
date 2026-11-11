import { fireEvent, screen, within } from '@testing-library/react';
import { act } from 'react';
import { beforeEach, describe, expect, it } from 'vitest';
import { axeResults } from '../test/axe';
import { refreshSucceeds } from '../test/handlers/auth';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';

const signedIn = { level: 1, name: 'On This Day' } as const;

async function openHome() {
  server.use(refreshSucceeds());
  const rendered = renderApp('/');
  expect(await screen.findByRole('heading', signedIn)).toBeVisible();
  return rendered;
}

describe('the bottom bar', () => {
  it('leads from Home to Today and Settings, with Today marked as the current page', async () => {
    const { container } = await openHome();

    expect(screen.getByRole('navigation')).not.toHaveAttribute('data-hidden');
    const bar = within(screen.getByRole('navigation'));
    expect(bar.getAllByRole('link').map((link) => link.textContent)).toEqual(['Today', 'Settings']);
    expect(bar.getByRole('link', { name: 'Today' })).toHaveAttribute('href', '/');
    expect(bar.getByRole('link', { name: 'Today' })).toHaveAttribute('aria-current', 'page');
    expect(bar.getByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings');
    expect(bar.getByRole('link', { name: 'Settings' })).not.toHaveAttribute('aria-current');
    expect(await axeResults(container)).toHaveNoViolations();
  });
});

async function openSettings(router: ReturnType<typeof renderApp>['router']) {
  fireEvent.click(screen.getByRole('link', { name: 'Settings' }));
  expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  expect(router.state.location.pathname).toBe('/settings');
}

function pageHeight(height: number) {
  Object.defineProperty(document.documentElement, 'scrollHeight', {
    value: height,
    configurable: true,
  });
}

function scrollTo(y: number) {
  act(() => {
    Object.defineProperty(window, 'scrollY', { value: y, configurable: true });
    fireEvent.scroll(window);
  });
}

const bar = () => screen.getByRole('navigation');

describe('the bottom bar while scrolling', () => {
  beforeEach(() => {
    pageHeight(3000);
  });

  it('stays for a scroll down of up to 8 px, and hides past it', async () => {
    await openHome();

    scrollTo(5);
    scrollTo(8);
    expect(bar()).not.toHaveAttribute('data-hidden');

    scrollTo(9);
    expect(bar()).toHaveAttribute('data-hidden');
  });

  it('stays hidden while the page keeps scrolling down, or stays where it is', async () => {
    await openHome();

    scrollTo(100);
    scrollTo(102);
    scrollTo(102);
    expect(bar()).toHaveAttribute('data-hidden');
  });

  it('comes back on any scroll up, and hides again past 8 px down from there', async () => {
    await openHome();
    scrollTo(500);

    scrollTo(499);
    expect(bar()).not.toHaveAttribute('data-hidden');

    scrollTo(507);
    expect(bar()).not.toHaveAttribute('data-hidden');
    scrollTo(508);
    expect(bar()).toHaveAttribute('data-hidden');
  });

  it('shows at the top, even after bouncing past it', async () => {
    await openHome();
    scrollTo(-30);
    scrollTo(-10);
    scrollTo(0);
    expect(bar()).not.toHaveAttribute('data-hidden');

    scrollTo(500);
    scrollTo(0);
    expect(bar()).not.toHaveAttribute('data-hidden');
  });

  it('stays hidden while the page bounces back from past its bottom', async () => {
    await openHome();

    scrollTo(2232);
    scrollTo(2260);
    scrollTo(2240);
    expect(bar()).toHaveAttribute('data-hidden');
  });

  it('stays on a page too short to scroll', async () => {
    pageHeight(768);
    await openHome();

    scrollTo(40);
    expect(bar()).not.toHaveAttribute('data-hidden');
  });

  it('reappears when keyboard focus enters it', async () => {
    await openHome();
    scrollTo(500);

    act(() => {
      screen.getByRole('link', { name: 'Today' }).focus();
    });
    expect(bar()).not.toHaveAttribute('data-hidden');

    scrollTo(505);
    expect(bar()).not.toHaveAttribute('data-hidden');
    scrollTo(509);
    expect(bar()).toHaveAttribute('data-hidden');
  });

  it('counts from where Home was left scrolled', async () => {
    scrollTo(300);
    await openHome();

    scrollTo(308);
    expect(bar()).not.toHaveAttribute('data-hidden');
    scrollTo(309);
    expect(bar()).toHaveAttribute('data-hidden');
  });
});

describe('a screen below Home', () => {
  it('has a top bar with its title and a back control, and the bottom bar', async () => {
    const { router, container } = await openHome();

    await openSettings(router);

    expect(screen.getByRole('banner')).toHaveTextContent('Settings');
    expect(within(screen.getByRole('banner')).getByRole('button', { name: 'Back' })).toBeVisible();
    expect(within(bar()).getByRole('link', { name: 'Settings' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(bar()).getByRole('link', { name: 'Today' })).not.toHaveAttribute('aria-current');
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('leads back to Home through the bottom bar', async () => {
    const { router } = await openHome();
    await openSettings(router);

    fireEvent.click(within(bar()).getByRole('link', { name: 'Today' }));

    expect(await screen.findByText(signedIn)).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
  });

  it('goes back in history when it was opened from Home', async () => {
    const { router } = await openHome();
    await openSettings(router);

    fireEvent.click(screen.getByRole('button', { name: 'Back' }));

    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
    expect(router.history.canGoBack()).toBe(false);
  });

  it('replaces itself with Home when it was opened directly', async () => {
    server.use(refreshSucceeds());
    const { router } = renderApp('/settings');
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();

    fireEvent.click(screen.getByRole('button', { name: 'Back' }));

    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    expect(router.history.length).toBe(1);
    expect(router.history.canGoBack()).toBe(false);
  });

  it('goes back in history to the Home that replaced it', async () => {
    server.use(refreshSucceeds());
    const { router } = renderApp('/settings');
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    await openSettings(router);

    fireEvent.click(screen.getByRole('button', { name: 'Back' }));

    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    expect(router.history.canGoBack()).toBe(false);
  });
});
