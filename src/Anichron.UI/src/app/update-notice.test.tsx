import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import { refreshSucceeds } from '../test/handlers/auth';
import { everyoneHasMemories } from '../test/handlers/flashbacks';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';
import { becomes } from '../test/visibility';

const notice = 'A new version is available';
const storyName = 'Memories of November 11, 2026';
const user = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });
const noticeDialog = () => screen.getByRole('dialog', { name: notice });
const button = (name: string) => within(noticeDialog()).getByRole('button', { name });

async function waiting() {
  const rendered = await openHome();
  rendered.updates.arrive();
  await act(() => vi.advanceTimersToNextFrame());
  return rendered;
}

async function openHome() {
  const rendered = renderApp('/');
  expect(await screen.findByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
  return rendered;
}

async function openStory() {
  const rendered = renderApp('/day/2026-11-11?year=2023');
  expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
  return rendered;
}

beforeEach(() => {
  vi.useFakeTimers({
    toFake: [
      'setTimeout',
      'clearTimeout',
      'setInterval',
      'clearInterval',
      'Date',
      'requestAnimationFrame',
      'cancelAnimationFrame',
    ],
  });
  vi.setSystemTime(new Date(2026, 10, 11, 8));
  server.use(refreshSucceeds(), everyoneHasMemories());
});

describe('the new-version notice', () => {
  it('stays away while the open version is the newest', async () => {
    await openHome();

    expect(screen.queryByText(notice)).not.toBeInTheDocument();
  });

  it('opens a dialog offering Reload or Later once a new version is waiting', async () => {
    const { container } = await waiting();

    expect(noticeDialog()).toBeVisible();
    expect(
      within(noticeDialog())
        .getAllByRole('button')
        .map((b) => b.textContent),
    ).toEqual(['Later', 'Reload']);
    expect(noticeDialog()).toContainElement(document.activeElement as HTMLElement);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('keeps focus inside the dialog', async () => {
    await waiting();

    await user().tab();
    await user().tab();
    await user().tab();

    expect(noticeDialog()).toContainElement(document.activeElement as HTMLElement);
  });

  it('applies the new version on Reload', async () => {
    const { updates } = await waiting();

    await user().click(button('Reload'));

    expect(updates.apply).toHaveBeenCalledOnce();
  });

  it.each([
    ['Later', () => user().click(button('Later'))],
    ['Escape', () => user().keyboard('{Escape}')],
    ['a tap outside', () => user().click(document.body)],
  ])('closes on %s without reloading', async (_, close) => {
    const { updates } = await waiting();
    expect(noticeDialog()).toBeVisible();

    await close();

    expect(screen.queryByRole('dialog', { name: notice })).not.toBeInTheDocument();
    expect(updates.apply).not.toHaveBeenCalled();
  });

  it('stays closed while the app stays open', async () => {
    const { updates } = await waiting();
    await user().click(button('Later'));

    updates.arrive();
    fireEvent.click(within(screen.getByRole('navigation')).getByRole('link', { name: 'Settings' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();

    expect(screen.queryByRole('dialog', { name: notice })).not.toBeInTheDocument();
  });

  it('comes back when the app is opened again from the background', async () => {
    await waiting();
    await user().click(button('Later'));

    act(() => {
      becomes('hidden');
    });
    expect(screen.queryByRole('dialog', { name: notice })).not.toBeInTheDocument();
    act(() => {
      becomes('visible');
    });

    expect(noticeDialog()).toBeVisible();
  });

  it('waits while a story is open, and shows once it closes', async () => {
    const { updates } = await openStory();

    updates.arrive();
    expect(screen.queryByText(notice)).not.toBeInTheDocument();

    fireEvent.click(
      within(screen.getByRole('dialog', { name: storyName })).getByRole('button', {
        name: 'Close',
      }),
    );

    expect(await screen.findByRole('dialog', { name: notice })).toBeVisible();
    expect(updates.apply).not.toHaveBeenCalled();
  });

  it('shows on Settings too', async () => {
    const { updates } = renderApp('/settings');
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();

    updates.arrive();

    expect(noticeDialog()).toBeVisible();
  });
});
