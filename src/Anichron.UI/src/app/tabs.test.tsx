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
import { axeResults } from '../test/axe';
import {
  assetIdOf,
  eachSeesTheirOwn,
  everyoneHasMemories,
  hold,
  memoriesOf,
} from '../test/handlers/flashbacks';
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
const loginHeading = { level: 1, name: 'Sign in to Anichron' } as const;
const unreachableHeading = { level: 1, name: "Can't reach Anichron" } as const;

let signedIn: string;
let events: string[];

const sessionOf = (username: string) =>
  HttpResponse.json({
    ...miraSession,
    accessToken: `token-${username}`,
    user: users.get(username) ?? miraSession.user,
  });

function refreshTakes(
  milliseconds: number,
  signedOutOn = new Set<number>(),
  unavailableOn = new Set<number>(),
) {
  let refreshes = 0;
  return refreshRespondsWith(async () => {
    const n = ++refreshes;
    events.push(`refresh ${String(n)} sent`);
    await delay(milliseconds);
    events.push(`refresh ${String(n)} answered`);
    if (unavailableOn.has(n)) return problemResponse(503, 'service_unavailable');
    return signedOutOn.has(n)
      ? problemResponse(401, 'refresh_token_required')
      : sessionOf(signedIn);
  });
}

function signInAsNoor(tab: ReturnType<typeof openTabs>[number]) {
  fireEvent.change(tab.getByLabelText('Username or email'), { target: { value: 'noor' } });
  fireEvent.change(tab.getByLabelText('Password'), { target: { value: 'hunter2' } });
  fireEvent.click(tab.getByRole('button', { name: 'Sign in' }));
}

const noorSignsIn = () =>
  loginRespondsWith(() => {
    signedIn = 'noor';
    return sessionOf('noor');
  });

async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

function openTabs(firstPath: string, secondPath: string) {
  const tabs = sharedTabs();
  const open = (path: string) => {
    const rendered = renderApp(path, tabs.tab());
    return {
      ...within(rendered.container),
      container: rendered.container,
      unmount: rendered.unmount,
    };
  };
  return [open(firstPath), open(secondPath)] as const;
}

beforeEach(() => {
  server.use(everyoneHasMemories());
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
    expect(await first.findByRole('link', memoriesOf('mira'))).toBeVisible();

    await advance(1000);
    expect(events).toEqual([
      'refresh 1 sent',
      'refresh 1 answered',
      'refresh 2 sent',
      'refresh 2 answered',
    ]);
    expect(await second.findByRole('link', memoriesOf('mira'))).toBeVisible();
  });

  it('are both signed out when one signs out', async () => {
    let logouts = 0;
    server.use(
      refreshTakes(0),
      logoutSucceeds(() => logouts++),
    );
    const [first, second] = openTabs('/settings', '/');
    expect(await first.findByRole('button', { name: 'Sign out' })).toBeVisible();
    expect(await second.findByRole('link', memoriesOf('mira'))).toBeVisible();

    fireEvent.click(first.getByRole('button', { name: 'Sign out' }));

    expect(await first.findByRole('heading', loginHeading)).toBeVisible();
    expect(await second.findByRole('heading', loginHeading)).toBeVisible();
    expect(second.queryByRole('link', memoriesOf('mira'))).toBeNull();
    expect(logouts).toBe(1);
  });

  it('start again as the user who signed in in the other', async () => {
    const answered: string[] = [];
    const holds = new Map<string, Promise<void>>();
    server.use(
      refreshTakes(1000, new Set([1])),
      loginRespondsWith(() => {
        signedIn = 'noor';
        return sessionOf('noor');
      }),
      eachSeesTheirOwn(answered, holds),
    );
    const [first, second] = openTabs('/login', '/');
    await advance(2000);
    expect(await first.findByRole('heading', loginHeading)).toBeVisible();
    expect(await second.findByRole('link', memoriesOf('mira'))).toBeVisible();
    events = [];

    fireEvent.change(first.getByLabelText('Username or email'), { target: { value: 'noor' } });
    fireEvent.change(first.getByLabelText('Password'), { target: { value: 'hunter2' } });
    fireEvent.click(first.getByRole('button', { name: 'Sign in' }));
    expect(await first.findByRole('link', memoriesOf('noor'))).toBeVisible();
    const noorsDay = hold();
    holds.set('noor', noorsDay.until);
    answered.length = 0;

    await advance(500);
    expect(events).toEqual(['refresh 3 sent']);
    expect(second.queryByRole('link', memoriesOf('mira'))).toBeNull();
    expect(second.queryByRole('link', memoriesOf('noor'))).toBeNull();

    await advance(1000);
    expect(second.queryByRole('link', memoriesOf('mira'))).toBeNull();
    expect(second.container.innerHTML).not.toContain(assetIdOf('mira'));

    noorsDay.release();
    expect(await second.findByRole('link', memoriesOf('noor'))).toBeVisible();
    expect(second.container.innerHTML).toContain(assetIdOf('noor'));
    expect(second.container.innerHTML).not.toContain(assetIdOf('mira'));
    expect(answered).not.toContain('mira');
    expect(answered).toContain('noor');
    first.unmount();
    expect(await axeResults(second.container)).toHaveNoViolations();
  });

  it('take a tab on the login screen into the app when the other signs in', async () => {
    server.use(refreshTakes(0, new Set([1, 2])), noorSignsIn());
    const [first, second] = openTabs('/login', '/login');
    expect(await first.findByRole('heading', loginHeading)).toBeVisible();
    expect(await second.findByRole('heading', loginHeading)).toBeVisible();

    signInAsNoor(first);

    expect(await first.findByRole('link', memoriesOf('noor'))).toBeVisible();
    expect(await second.findByRole('link', memoriesOf('noor'))).toBeVisible();
    expect(second.queryByRole('heading', loginHeading)).toBeNull();
  });

  it('leave a tab saying so, without retrying, when its fresh start cannot reach the server', async () => {
    server.use(refreshTakes(0, new Set([1]), new Set([3])), noorSignsIn());
    const [first, second] = openTabs('/login', '/');
    expect(await first.findByRole('heading', loginHeading)).toBeVisible();
    expect(await second.findByRole('link', memoriesOf('mira'))).toBeVisible();
    events = [];

    signInAsNoor(first);
    expect(await first.findByRole('link', memoriesOf('noor'))).toBeVisible();
    expect(await second.findByRole('heading', unreachableHeading)).toBeVisible();

    await advance(60_000);
    expect(events).toEqual(['refresh 3 sent', 'refresh 3 answered']);
    expect(second.getByRole('heading', unreachableHeading)).toBeVisible();
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
    expect(await second.findByRole('link', memoriesOf('mira'))).toBeVisible();
    second.unmount();
    events = [];

    fireEvent.change(first.getByLabelText('Username or email'), { target: { value: 'noor' } });
    fireEvent.change(first.getByLabelText('Password'), { target: { value: 'hunter2' } });
    fireEvent.click(first.getByRole('button', { name: 'Sign in' }));
    expect(await first.findByRole('link', memoriesOf('noor'))).toBeVisible();
    await advance(1000);

    expect(events).toEqual([]);
  });
});
