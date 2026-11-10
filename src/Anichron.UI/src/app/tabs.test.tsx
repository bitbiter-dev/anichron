import { fireEvent, within } from '@testing-library/react';
import { delay, HttpResponse } from 'msw';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { components } from '../api/generated/v1';
import {
  loginRespondsWith,
  logoutSucceeds,
  miraSession,
  problemResponse,
  refreshRespondsWith,
} from '../test/handlers/auth';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';
import { sharedTabs } from '../test/tabs';

type UserProfile = components['schemas']['UserProfileResponse'];

const noor: UserProfile = {
  ...miraSession.user,
  id: '9b2f4c1e-7d3a-4e8b-a6f5-0c1d2e3f4a5b',
  username: 'noor',
  email: 'noor@example.com',
};
const users = new Map([
  ['mira', miraSession.user],
  ['noor', noor],
]);
const greeting = (username: string) => `Signed in as ${username}.`;
const loginHeading = { level: 1, name: 'Sign in to Anichron' } as const;

let signedIn: string;
let events: string[];

const sessionOf = (username: string) =>
  HttpResponse.json({
    ...miraSession,
    accessToken: `token-${username}`,
    user: users.get(username) ?? miraSession.user,
  });

function refreshTakes(milliseconds: number, signedOutOn = new Set<number>()) {
  let refreshes = 0;
  return refreshRespondsWith(async () => {
    const n = ++refreshes;
    events.push(`refresh ${String(n)} sent`);
    await delay(milliseconds);
    events.push(`refresh ${String(n)} answered`);
    return signedOutOn.has(n)
      ? problemResponse(401, 'refresh_token_required')
      : sessionOf(signedIn);
  });
}

async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

function openTabs(firstPath: string, secondPath: string) {
  const tabs = sharedTabs();
  const open = (path: string) => {
    const rendered = renderApp(path, tabs.tab());
    return { ...within(rendered.container), unmount: rendered.unmount };
  };
  return [open(firstPath), open(secondPath)] as const;
}

beforeEach(() => {
  signedIn = 'mira';
  events = [];
});

describe('two tabs of one browser', () => {
  it('refresh one after the other, each with the cookie the other left', async () => {
    server.use(refreshTakes(1000));

    const [first, second] = openTabs('/', '/');
    await advance(500);
    expect(events).toEqual(['refresh 1 sent']);

    await advance(1000);
    expect(events).toEqual(['refresh 1 sent', 'refresh 1 answered', 'refresh 2 sent']);
    expect(await first.findByText(greeting('mira'))).toBeVisible();

    await advance(1000);
    expect(events).toEqual([
      'refresh 1 sent',
      'refresh 1 answered',
      'refresh 2 sent',
      'refresh 2 answered',
    ]);
    expect(await second.findByText(greeting('mira'))).toBeVisible();
  });

  it('are both signed out when one signs out', async () => {
    let logouts = 0;
    server.use(
      refreshTakes(0),
      logoutSucceeds(() => logouts++),
    );
    const [first, second] = openTabs('/settings', '/');
    expect(await first.findByRole('button', { name: 'Sign out' })).toBeVisible();
    expect(await second.findByText(greeting('mira'))).toBeVisible();

    fireEvent.click(first.getByRole('button', { name: 'Sign out' }));

    expect(await first.findByRole('heading', loginHeading)).toBeVisible();
    expect(await second.findByRole('heading', loginHeading)).toBeVisible();
    expect(second.queryByText(greeting('mira'))).toBeNull();
    expect(logouts).toBe(1);
  });

  it('start again as the user who signed in in the other', async () => {
    server.use(
      refreshTakes(1000, new Set([1])),
      loginRespondsWith(() => {
        signedIn = 'noor';
        return sessionOf('noor');
      }),
    );
    const [first, second] = openTabs('/login', '/');
    await advance(2000);
    expect(await first.findByRole('heading', loginHeading)).toBeVisible();
    expect(await second.findByText(greeting('mira'))).toBeVisible();
    events = [];

    fireEvent.change(first.getByLabelText('Username or email'), { target: { value: 'noor' } });
    fireEvent.change(first.getByLabelText('Password'), { target: { value: 'hunter2' } });
    fireEvent.click(first.getByRole('button', { name: 'Sign in' }));
    expect(await first.findByText(greeting('noor'))).toBeVisible();

    await advance(500);
    expect(events).toEqual(['refresh 3 sent']);
    expect(second.queryByText(greeting('mira'))).toBeNull();
    expect(second.queryByText(greeting('noor'))).toBeNull();

    await advance(500);
    expect(await second.findByText(greeting('noor'))).toBeVisible();
  });

  it('leave a tab that was closed alone', async () => {
    server.use(
      refreshTakes(0, new Set([1])),
      loginRespondsWith(() => {
        signedIn = 'noor';
        return sessionOf('noor');
      }),
    );
    const [first, second] = openTabs('/login', '/');
    expect(await first.findByRole('heading', loginHeading)).toBeVisible();
    expect(await second.findByText(greeting('mira'))).toBeVisible();
    second.unmount();
    events = [];

    fireEvent.change(first.getByLabelText('Username or email'), { target: { value: 'noor' } });
    fireEvent.change(first.getByLabelText('Password'), { target: { value: 'hunter2' } });
    fireEvent.click(first.getByRole('button', { name: 'Sign in' }));
    expect(await first.findByText(greeting('noor'))).toBeVisible();
    await advance(1000);

    expect(events).toEqual([]);
  });
});
