import { screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { HttpResponse } from 'msw';
import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { LoginRequest } from '../../api/client';
import { axeResults } from '../../test/axe';
import { loginFails, loginRespondsWith, loginSucceeds, problem } from '../../test/handlers/auth';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';

const serverText = 'Server text that is never shown.';

const submitButton = () => screen.getByRole('button', { name: 'Sign in' });

const user = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });

async function openLogin() {
  const rendered = renderApp('/login');
  await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });
  return rendered;
}

async function fillIn(usernameOrEmail: string, password: string) {
  const typing = user();
  await typing.type(screen.getByLabelText('Username or email'), usernameOrEmail);
  if (password) await typing.type(screen.getByLabelText('Password'), password);
}

async function signIn(usernameOrEmail: string, password: string) {
  await fillIn(usernameOrEmail, password);
  await user().click(submitButton());
}

async function alertSays(message: string) {
  await waitFor(() => {
    expect(screen.getByRole('alert')).toHaveTextContent(message);
  });
}

describe('the login screen', () => {
  it('is labelled and accessible', async () => {
    const { container } = await openLogin();

    expect(screen.getByLabelText('Username or email')).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
    expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'current-password');
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('signs in with the trimmed username and lands on the signed-in page', async () => {
    const sent: LoginRequest[] = [];
    server.use(loginSucceeds((request) => sent.push(request)));
    const { router, container } = await openLogin();

    await signIn('  mira  ', 'correct horse battery staple');

    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
    expect(await axeResults(container)).toHaveNoViolations();
    expect(sent).toEqual([{ usernameOrEmail: 'mira', password: 'correct horse battery staple' }]);
  });

  it('asks for both fields before sending anything', async () => {
    const { container } = await openLogin();

    await signIn('   ', '');

    await waitFor(() => {
      expect(screen.getAllByText('Fill in this field.')).toHaveLength(2);
    });
    expect(screen.getByLabelText('Username or email')).toHaveAccessibleDescription(
      'Fill in this field.',
    );
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Username or email')).toHaveFocus();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('moves through the form with Tab in reading order', async () => {
    await openLogin();
    const keyboard = user();

    await keyboard.tab();
    expect(screen.getByLabelText('Username or email')).toHaveFocus();
    await keyboard.tab();
    expect(screen.getByLabelText('Password')).toHaveFocus();
    await keyboard.tab();
    expect(submitButton()).toHaveFocus();
  });

  it('signs in with Enter from the password field', async () => {
    const sent: LoginRequest[] = [];
    server.use(loginSucceeds((request) => sent.push(request)));
    await openLogin();

    await fillIn('mira', 'correct horse battery staple{Enter}');

    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();
    expect(sent).toEqual([{ usernameOrEmail: 'mira', password: 'correct horse battery staple' }]);
  });

  it('signs in with Enter on the focused button', async () => {
    server.use(loginSucceeds());
    await openLogin();
    await fillIn('mira', 'correct horse battery staple');
    const keyboard = user();

    await keyboard.tab();
    expect(submitButton()).toHaveFocus();
    await keyboard.keyboard('{Enter}');

    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();
  });

  it.each([
    [401, 'invalid_credentials', 'The username, email or password is wrong.'],
    [401, 'account_disabled', 'This account is disabled. Ask your administrator to enable it.'],
    [500, 'a_code_from_the_future', 'Something went wrong. Try again.'],
  ])('shows a %i %s in the user’s language', async (status, code, message) => {
    server.use(loginFails(status, problem(status, code)));
    const { router } = await openLogin();

    await signIn('mira', 'wrong');

    await alertSays(message);
    expect(screen.queryByText(serverText)).toBeNull();
    expect(router.state.location.pathname).toBe('/login');
    expect(submitButton()).toBeEnabled();
    expect(screen.getByLabelText('Username or email')).toHaveAttribute('aria-invalid', 'false');
  });

  it.each([
    [
      'a body that is not JSON',
      () => new HttpResponse('<html>Bad gateway</html>', { status: 502 }),
    ],
    [
      'a body that is not a problem',
      () => HttpResponse.json(['invalid_credentials'], { status: 401 }),
    ],
  ])('shows the generic error for %s', async (_, response) => {
    server.use(loginRespondsWith(response));
    await openLogin();

    await signIn('mira', 'wrong');

    await alertSays('Something went wrong. Try again.');
    expect(submitButton()).toBeEnabled();
  });

  it('counts down an unavailable service from Retry-After, then accepts a new attempt', async () => {
    server.use(loginFails(503, problem(503, 'service_unavailable'), { 'Retry-After': '5' }));
    const { container } = await openLogin();

    await signIn('mira', 'correct horse battery staple');

    await alertSays('Anichron is temporarily unavailable. Try again shortly.');
    expect(screen.getByText('You can try again in 5 seconds.')).toBeVisible();
    expect(submitButton()).toBeDisabled();
    expect(await axeResults(container)).toHaveNoViolations();

    await act(() => vi.advanceTimersByTimeAsync(5000));
    expect(submitButton()).toBeEnabled();
    server.use(loginSucceeds());
    await user().click(submitButton());
    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();
  });

  it('shows the unavailable message without a countdown when the server cannot be reached', async () => {
    server.use(loginRespondsWith(() => HttpResponse.error()));
    const { container } = await openLogin();

    await signIn('mira', 'correct horse battery staple');

    await alertSays('Anichron is temporarily unavailable. Try again shortly.');
    expect(screen.queryByText(/You can try again/)).toBeNull();
    expect(submitButton()).toBeEnabled();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('shows the generic error when a successful sign-in has an unreadable body', async () => {
    server.use(loginRespondsWith(() => new HttpResponse('not json', { status: 200 })));
    await openLogin();

    await signIn('mira', 'correct horse battery staple');

    await alertSays('Something went wrong. Try again.');
  });

  it('shows a field error from the API on its field', async () => {
    server.use(
      loginFails(
        422,
        problem(422, 'a_code_from_the_future', {
          errors: { password: ['invalid_credentials', 'a_code_from_the_future'], other: ['x'] },
        }),
      ),
    );
    await openLogin();

    await signIn('mira', 'short');

    await waitFor(() => {
      expect(screen.getByLabelText('Password')).toHaveAccessibleDescription(
        'The username, email or password is wrong.',
      );
    });
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Username or email')).not.toHaveAccessibleDescription();
  });

  it.each([
    ['a single code', 'invalid_credentials'],
    ['an empty list', []],
  ])('ignores field errors given as %s', async (_, password) => {
    server.use(loginFails(422, problem(422, 'invalid_credentials', { errors: { password } })));
    await openLogin();

    await signIn('mira', 'short');

    await alertSays('The username, email or password is wrong.');
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'false');
  });

  it('counts down a locked account from Retry-After, then accepts a new attempt', async () => {
    server.use(loginFails(429, problem(429, 'account_locked'), { 'Retry-After': '3' }));
    const { container } = await openLogin();

    await signIn('mira', 'wrong');

    await alertSays('Too many failed attempts. This account is locked for now.');
    expect(screen.getByText('You can try again in 3 seconds.')).toBeVisible();
    expect(submitButton()).toBeDisabled();

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.getByText('You can try again in 2 seconds.')).toBeVisible();
    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.getByText('You can try again in 1 second.')).toBeVisible();
    expect(submitButton()).toBeDisabled();
    await act(() => vi.advanceTimersByTimeAsync(1000));

    expect(screen.queryByText(/You can try again/)).toBeNull();
    expect(submitButton()).toBeEnabled();
    expect(await axeResults(container)).toHaveNoViolations();
    server.use(loginSucceeds());
    await user().click(submitButton());
    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();
  });

  it('counts down a rate limit from Retry-After', async () => {
    server.use(loginFails(429, problem(429, 'rate_limited'), { 'Retry-After': '60' }));
    await openLogin();

    await signIn('mira', 'wrong');

    await alertSays('Too many attempts from this device.');
    expect(screen.getByText('You can try again in 60 seconds.')).toBeVisible();
    await act(() => vi.advanceTimersByTimeAsync(59_000));
    expect(screen.getByText('You can try again in 1 second.')).toBeVisible();
  });

  it('shows no countdown when Retry-After is missing or unreadable', async () => {
    server.use(loginFails(429, problem(429, 'rate_limited'), { 'Retry-After': 'soon' }));
    await openLogin();

    await signIn('mira', 'wrong');

    await alertSays('Too many attempts from this device.');
    expect(screen.queryByText(/You can try again/)).toBeNull();
    expect(submitButton()).toBeEnabled();
  });
});
