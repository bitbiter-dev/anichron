import { fireEvent, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import { refreshSucceeds } from '../test/handlers/auth';
import { everyoneHasMemories } from '../test/handlers/flashbacks';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';

const notice = 'A new version is available';
const storyName = 'Memories of November 11, 2026';

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
  vi.setSystemTime(new Date(2026, 10, 11, 8));
  server.use(refreshSucceeds(), everyoneHasMemories());
});

describe('the new-version notice', () => {
  it('stays away while the open version is the newest', async () => {
    await openHome();

    expect(screen.queryByText(notice)).not.toBeInTheDocument();
  });

  it('offers a reload once a new version is waiting, with the bottom bar still there', async () => {
    const { container, updates } = await openHome();

    updates.arrive();

    const status = screen.getByRole('status');
    expect(within(status).getByText(notice)).toBeVisible();
    expect(within(status).getByRole('button', { name: 'Reload' })).toBeVisible();
    expect(
      within(screen.getByRole('navigation')).getByRole('link', { name: 'Today' }),
    ).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('applies the new version on Reload', async () => {
    const { updates } = await openHome();
    updates.arrive();

    fireEvent.click(screen.getByRole('button', { name: 'Reload' }));

    expect(updates.apply).toHaveBeenCalledOnce();
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

    expect(await screen.findByText(notice)).toBeVisible();
    expect(updates.apply).not.toHaveBeenCalled();
  });

  it('shows on Settings too', async () => {
    const { updates } = renderApp('/settings');
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();

    updates.arrive();

    expect(screen.getByText(notice)).toBeVisible();
  });
});
