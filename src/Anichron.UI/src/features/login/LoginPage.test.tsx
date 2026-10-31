import { fireEvent, screen, waitFor } from '@testing-library/react';
import { HttpResponse } from 'msw';
import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { LoginRequest } from '../../api/login-contract';
import { axeResults } from '../../test/axe';
import { loginFails, loginRespondsWith, loginSucceeds, problem } from '../../test/handlers/login';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';

const serverText = 'Server text that is never shown.';

async function openLogin() {
  const rendered = renderApp('/login');
  await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });
  return rendered;
}

function signIn(usernameOrEmail: string, password: string) {
  fireEvent.change(screen.getByLabelText('Username or email'), {
    target: { value: usernameOrEmail },
  });
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

async function alertSays(message: string) {
  await waitFor(() => {
    expect(screen.getByRole('alert')).toHaveTextContent(message);
  });
}

const submitButton = () => screen.getByRole('button', { name: 'Sign in' });

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

    signIn('  mira  ', 'correct horse battery staple');

    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
    expect(await axeResults(container)).toHaveNoViolations();
    expect(sent).toEqual([{ usernameOrEmail: 'mira', password: 'correct horse battery staple' }]);
  });

  it('asks for both fields before sending anything', async () => {
    const { container } = await openLogin();

    signIn('   ', '');

    await waitFor(() => {
      expect(screen.getAllByText('Fill in this field.')).toHaveLength(2);
    });
    expect(screen.getByLabelText('Username or email')).toHaveAccessibleDescription(
      'Fill in this field.',
    );
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true');
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it.each([
    [401, 'invalid_credentials', 'The username, email or password is wrong.'],
    [401, 'account_disabled', 'This account is disabled. Ask your administrator to enable it.'],
    [500, 'a_code_from_the_future', 'Something went wrong. Try again.'],
  ])('shows a %i %s in the user’s language', async (status, code, message) => {
    server.use(loginFails(status, problem(status, code)));
    const { router } = await openLogin();

    signIn('mira', 'wrong');

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
    ['a network failure', () => HttpResponse.error()],
  ])('shows the generic error for %s', async (_, response) => {
    server.use(loginRespondsWith(response));
    await openLogin();

    signIn('mira', 'wrong');

    await alertSays('Something went wrong. Try again.');
    expect(submitButton()).toBeEnabled();
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

    signIn('mira', 'short');

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

    signIn('mira', 'short');

    await alertSays('The username, email or password is wrong.');
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'false');
  });

  it('counts down a locked account from Retry-After, then accepts a new attempt', async () => {
    server.use(loginFails(429, problem(429, 'account_locked'), { 'Retry-After': '3' }));
    const { container } = await openLogin();

    signIn('mira', 'wrong');

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
    fireEvent.click(submitButton());
    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();
  });

  it('counts down a rate limit from Retry-After', async () => {
    server.use(loginFails(429, problem(429, 'rate_limited'), { 'Retry-After': '60' }));
    await openLogin();

    signIn('mira', 'wrong');

    await alertSays('Too many attempts from this device.');
    expect(screen.getByText('You can try again in 60 seconds.')).toBeVisible();
    await act(() => vi.advanceTimersByTimeAsync(59_000));
    expect(screen.getByText('You can try again in 1 second.')).toBeVisible();
  });

  it('shows no countdown when Retry-After is missing or unreadable', async () => {
    server.use(loginFails(429, problem(429, 'rate_limited'), { 'Retry-After': 'soon' }));
    await openLogin();

    signIn('mira', 'wrong');

    await alertSays('Too many attempts from this device.');
    expect(screen.queryByText(/You can try again/)).toBeNull();
    expect(submitButton()).toBeEnabled();
  });
});
