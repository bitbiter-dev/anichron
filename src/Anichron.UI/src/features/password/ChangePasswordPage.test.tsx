import { screen, waitFor, within } from '@testing-library/react';
import { act } from 'react';
import { HttpResponse } from 'msw';
import { describe, expect, it, onTestFinished } from 'vitest';
import { axeResults } from '../../test/axe';
import {
  forcedAdminSession,
  adminSession,
  forcedSession,
  loginSucceeds,
  logoutSucceeds,
  miraSession,
  passwordChangeFails,
  passwordChangeRespondsWith,
  passwordChangeSucceeds,
  problem,
  refreshFails,
  refreshRespondsWith,
  refreshSucceeds,
} from '../../test/handlers/auth';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { sharedTabs } from '../../test/tabs';
import { advance, user } from '../../test/time';

const submitButton = () => screen.getByRole('button', { name: 'Change password' });
const home = { level: 1, name: 'On This Day' } as const;
const passwordChanged = 'Password changed.';
const toastSays = (message: string) => screen.findByText(message);

async function openVoluntary() {
  server.use(refreshSucceeds());
  const rendered = renderApp('/settings/password');
  await screen.findByRole('heading', { level: 1, name: 'Change password' });
  return rendered;
}

async function openForced() {
  server.use(refreshSucceeds(forcedSession));
  const rendered = renderApp('/change-password');
  await screen.findByRole('heading', { level: 1, name: 'Choose a new password' });
  return rendered;
}

async function fillIn(current: string, next: string, confirm: string) {
  const typing = user();
  if (current) await typing.type(screen.getByLabelText('Current password'), current);
  if (next) await typing.type(screen.getByLabelText('New password'), next);
  if (confirm) await typing.type(screen.getByLabelText('Confirm new password'), confirm);
}

async function change(current: string, next: string, confirm = next) {
  await fillIn(current, next, confirm);
  await user().click(submitButton());
}

async function alertSays(message: string) {
  await waitFor(() => {
    expect(screen.getByRole('alert')).toHaveTextContent(message);
  });
}

async function fieldSays(label: string, message: string) {
  await waitFor(() => {
    expect(screen.getByLabelText(label)).toHaveAccessibleDescription(message);
  });
  expect(screen.getByLabelText(label)).toHaveAttribute('aria-invalid', 'true');
}

interface Sent {
  body: unknown;
  bearer: string | null;
}

describe('changing the password from settings', () => {
  it('is reached from the account settings, and goes back to them', async () => {
    server.use(refreshSucceeds());
    const { router } = renderApp('/settings');

    await user().click(await screen.findByRole('link', { name: 'Change password' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Change password' })).toBeVisible();
    expect(router.state.location.pathname).toBe('/settings/password');
    await user().click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  });

  it('labels its fields for password managers', async () => {
    const { container } = await openVoluntary();

    for (const label of ['Current password', 'New password', 'Confirm new password'])
      expect(screen.getByLabelText(label)).toHaveAttribute('type', 'password');
    expect(screen.getByLabelText('Current password')).toHaveAttribute(
      'autocomplete',
      'current-password',
    );
    expect(screen.getByLabelText('New password')).toHaveAttribute('autocomplete', 'new-password');
    expect(screen.getByLabelText('Confirm new password')).toHaveAttribute(
      'autocomplete',
      'new-password',
    );
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('sends the current and new password with the session, then goes home with a toast', async () => {
    const sent: Sent[] = [];
    server.use(
      refreshSucceeds(),
      passwordChangeSucceeds((body, bearer) => sent.push({ body, bearer })),
    );
    const { container, router } = renderApp('/settings');
    await user().click(await screen.findByRole('link', { name: 'Change password' }));
    await screen.findByRole('heading', { level: 1, name: 'Change password' });

    await change('old secret', 'correct horse battery staple');

    expect(await screen.findByRole('heading', home)).toBeVisible();
    expect(await toastSays(passwordChanged)).toBeVisible();
    expect(router.state.location.href).toBe('/');
    expect(sent).toEqual([
      {
        body: { currentPassword: 'old secret', newPassword: 'correct horse battery staple' },
        bearer: 'Bearer header.payload.signature',
      },
    ]);
    expect(await axeResults(container)).toHaveNoViolations();

    act(() => {
      router.history.back();
    });

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    expect(router.state.location.pathname).toBe('/settings');
  });

  it('keeps this device signed in with the new session', async () => {
    const bearers: (string | null)[] = [];
    server.use(passwordChangeSucceeds((_, bearer) => bearers.push(bearer)));
    await openVoluntary();

    await change('old secret', 'correct horse battery staple');
    await screen.findByRole('heading', home);
    await user().click(screen.getByRole('link', { name: 'Settings' }));
    await user().click(await screen.findByRole('link', { name: 'Change password' }));
    await screen.findByRole('heading', { level: 1, name: 'Change password' });
    await change('correct horse battery staple', 'another staple');

    await waitFor(() => {
      expect(bearers).toEqual([
        'Bearer header.payload.signature',
        'Bearer header.changed.signature',
      ]);
    });
  });

  it('leaves its toast behind for the next user', async () => {
    server.use(passwordChangeSucceeds(), logoutSucceeds(), loginSucceeds(undefined, adminSession));
    await openVoluntary();
    await change('old secret', 'correct horse battery staple');
    expect(await toastSays(passwordChanged)).toBeVisible();

    await user().click(screen.getByRole('link', { name: 'Settings' }));
    await user().click(await screen.findByRole('button', { name: 'Sign out' }));
    await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });
    await user().type(screen.getByLabelText('Username or email'), 'alice');
    await user().type(screen.getByLabelText('Password'), 'secret');
    await user().click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('heading', home)).toBeVisible();
    expect(screen.queryByText(passwordChanged)).toBeNull();
  });

  it('asks for every field before sending anything', async () => {
    const sent: Sent[] = [];
    server.use(passwordChangeSucceeds((body, bearer) => sent.push({ body, bearer })));
    const { container } = await openVoluntary();

    await user().click(submitButton());

    await waitFor(() => {
      expect(screen.getAllByText('Fill in this field.')).toHaveLength(3);
    });
    expect(screen.getByLabelText('Current password')).toHaveFocus();
    expect(await axeResults(container)).toHaveNoViolations();
    expect(sent).toEqual([]);
  });

  it('sends nothing when the new passwords differ', async () => {
    const sent: Sent[] = [];
    server.use(passwordChangeSucceeds((body, bearer) => sent.push({ body, bearer })));
    await openVoluntary();

    await change('old secret', 'correct horse battery staple', 'correct horse battery stapel');

    await fieldSays('Confirm new password', "The new passwords don't match.");
    expect(screen.getByLabelText('New password')).toHaveAttribute('aria-invalid', 'false');
    expect(sent).toEqual([]);
  });

  it('shows a wrong current password on its field', async () => {
    server.use(
      passwordChangeFails(
        400,
        problem(400, 'invalid_credentials', {
          errors: { currentPassword: ['invalid_credentials'] },
        }),
      ),
    );
    const { container, router } = await openVoluntary();

    await change('wrong', 'correct horse battery staple');

    await fieldSays('Current password', 'The current password is wrong.');
    expect(router.state.location.pathname).toBe('/settings/password');
    expect(screen.getByRole('alert')).toBeEmptyDOMElement();
    expect(screen.getByLabelText('New password')).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByText('Server text that is never shown.')).toBeNull();
    expect(submitButton()).toBeEnabled();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it.each([
    ['password_too_short', 'This password is too short.'],
    ['password_too_long', 'This password is too long.'],
    ['password_pwned', 'This password has appeared in a data breach. Choose another.'],
  ])('shows %s on the new password', async (code, message) => {
    server.use(passwordChangeFails(422, problem(422, code, { errors: { newPassword: [code] } })));
    await openVoluntary();

    await change('old secret', 'short');

    await fieldSays('New password', message);
    expect(screen.getByLabelText('Current password')).toHaveAttribute('aria-invalid', 'false');
    expect(screen.getByRole('alert')).toBeEmptyDOMElement();
  });

  it.each([
    [500, 'a_code_from_the_future', 'Something went wrong. Try again.'],
    [503, 'service_unavailable', 'Anichron is temporarily unavailable. Try again shortly.'],
  ])('shows a %i %s above the form', async (status, code, message) => {
    server.use(passwordChangeFails(status, problem(status, code)));
    await openVoluntary();

    await change('old secret', 'correct horse battery staple');

    await alertSays(message);
    expect(screen.queryByText(/You can try again/)).toBeNull();
    expect(submitButton()).toBeEnabled();
  });

  it('counts down a rate limit from Retry-After, then accepts a new attempt', async () => {
    server.use(passwordChangeFails(429, problem(429, 'rate_limited'), { 'Retry-After': '2' }));
    const { container } = await openVoluntary();

    await change('old secret', 'correct horse battery staple');

    await alertSays('Too many attempts from this device.');
    expect(screen.getByText('You can try again in 2 seconds.')).toBeVisible();
    expect(submitButton()).toBeDisabled();
    expect(await axeResults(container)).toHaveNoViolations();
    await advance(2000);
    expect(submitButton()).toBeEnabled();
    server.use(passwordChangeSucceeds());
    await user().click(submitButton());
    expect(await screen.findByRole('heading', home)).toBeVisible();
  });

  it('is sent again after a refresh when the access token has expired', async () => {
    const bearers: (string | null)[] = [];
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() =>
        HttpResponse.json({ ...miraSession, accessToken: `token-${String(++refreshes)}` }),
      ),
      passwordChangeRespondsWith((bearer) => {
        bearers.push(bearer);
        return bearers.length === 1
          ? HttpResponse.json({ status: 401 }, { status: 401 })
          : HttpResponse.json(miraSession);
      }),
    );
    renderApp('/settings/password');
    await screen.findByRole('heading', { level: 1, name: 'Change password' });

    await change('old secret', 'correct horse battery staple');

    expect(await screen.findByRole('heading', home)).toBeVisible();
    expect(bearers).toEqual(['Bearer token-1', 'Bearer token-2']);
  });

  it('starts every other tab again on the new session', async () => {
    const tabs = sharedTabs();
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() => {
        refreshes++;
        return HttpResponse.json(miraSession);
      }),
      passwordChangeSucceeds(),
    );
    const other = renderApp('/settings', tabs.tab());
    await within(other.container).findByRole('heading', { level: 1, name: 'Settings' });
    const self = renderApp('/settings/password', tabs.tab());
    const form = within(self.container);
    await form.findByRole('heading', { level: 1, name: 'Change password' });
    const typing = user();
    await typing.type(form.getByLabelText('Current password'), 'old secret');
    await typing.type(form.getByLabelText('New password'), 'staple');
    await typing.type(form.getByLabelText('Confirm new password'), 'staple');

    await typing.click(form.getByRole('button', { name: 'Change password' }));

    expect(await form.findByRole('heading', home)).toBeVisible();
    await waitFor(() => {
      expect(refreshes).toBe(3);
    });
    expect(
      await within(other.container).findByRole('heading', { level: 1, name: 'Settings' }),
    ).toBeVisible();
  });
});

function dataRequests() {
  const paths: string[] = [];
  const record = ({ request }: { request: Request }) => {
    const { pathname } = new URL(request.url);
    if (!pathname.startsWith('/api/v1/auth/')) paths.push(pathname);
  };
  server.events.on('request:start', record);
  onTestFinished(() => {
    server.events.removeListener('request:start', record);
  });
  return paths;
}

describe('a password that must be changed', () => {
  it.each([
    ['/', '%2F'],
    ['/day/2026-11-11?year=2023', '%2Fday%2F2026-11-11%3Fyear%3D2023'],
    ['/settings', '%2Fsettings'],
    ['/settings/password', '%2Fsettings%2Fpassword'],
    ['/hidden', '%2Fhidden'],
    ['/admin/work-items', '%2Fadmin%2Fwork-items'],
  ])('turns a start on %s into the change, and asks for no data', async (path, encoded) => {
    const requests = dataRequests();
    server.use(refreshSucceeds(forcedAdminSession));
    const { container, router } = renderApp(path);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Choose a new password' }),
    ).toBeVisible();
    expect(router.state.location.href).toBe(`/change-password?redirect=${encoded}`);
    expect(screen.queryByRole('link', { name: 'Settings' })).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
    expect(requests).toEqual([]);
  });

  it.each([
    ['/', '%2F'],
    ['/day/2026-11-11?year=2023', '%2Fday%2F2026-11-11%3Fyear%3D2023'],
    ['/settings', '%2Fsettings'],
    ['/admin/work-items', '%2Fadmin%2Fwork-items'],
  ])('turns a sign-in on %s into the change, keeping the address', async (path, encoded) => {
    const requests = dataRequests();
    server.use(refreshFails(), loginSucceeds(undefined, forcedAdminSession));
    const { router } = renderApp(path);
    await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });

    await user().type(screen.getByLabelText('Username or email'), 'mira');
    await user().type(screen.getByLabelText('Password'), 'temporary');
    await user().click(screen.getByRole('button', { name: 'Sign in' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Choose a new password' }),
    ).toBeVisible();
    expect(router.state.location.href).toBe(`/change-password?redirect=${encoded}`);
    expect(requests).toEqual([]);
  });

  it('opens the requested page after the change, with a toast', async () => {
    server.use(refreshSucceeds(forcedSession), passwordChangeSucceeds());
    const { container, router } = renderApp('/settings');
    await screen.findByRole('heading', { level: 1, name: 'Choose a new password' });

    await change('temporary', 'correct horse battery staple');

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    expect(await toastSays(passwordChanged)).toBeVisible();
    expect(router.state.location.href).toBe('/settings');
    expect(router.history.length).toBe(1);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it.each([
    ['another site', '//evil.example'],
    ['a full address', 'https%3A%2F%2Fevil.example%2F'],
  ])('opens Home after the change when the return address is %s', async (_, redirect) => {
    server.use(refreshSucceeds(forcedSession), passwordChangeSucceeds());
    const { router } = renderApp(`/change-password?redirect=${redirect}`);
    await screen.findByRole('heading', { level: 1, name: 'Choose a new password' });

    await change('temporary', 'correct horse battery staple');

    expect(await screen.findByRole('heading', home)).toBeVisible();
    expect(router.state.location.href).toBe('/');
  });

  it('is changed on its own screen, outside the app, which then opens', async () => {
    const sent: Sent[] = [];
    server.use(passwordChangeSucceeds((body, bearer) => sent.push({ body, bearer })));
    const { container, router } = await openForced();

    expect(screen.getByText('You need a new password before you continue.')).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Settings' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();

    await change('temporary', 'correct horse battery staple');

    expect(await screen.findByRole('heading', home)).toBeVisible();
    expect(await toastSays(passwordChanged)).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
    expect(router.history.length).toBe(1);
    expect(sent).toEqual([
      {
        body: { currentPassword: 'temporary', newPassword: 'correct horse battery staple' },
        bearer: 'Bearer header.payload.signature',
      },
    ]);
  });

  it('shows the same errors as from settings', async () => {
    server.use(
      passwordChangeFails(
        422,
        problem(422, 'password_pwned', { errors: { newPassword: ['password_pwned'] } }),
      ),
    );
    const { router } = await openForced();

    await change('temporary', 'password');

    await fieldSays('New password', 'This password has appeared in a data breach. Choose another.');
    expect(router.state.location.pathname).toBe('/change-password');
  });

  it('can be left only by signing out', async () => {
    server.use(logoutSucceeds());
    const { router } = await openForced();

    await user().click(screen.getByRole('button', { name: 'Sign out' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/login');
  });

  it('is not shown to a user who has nothing to change', async () => {
    server.use(refreshSucceeds());
    const { router } = renderApp('/change-password');

    expect(await screen.findByRole('heading', home)).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
  });
});
