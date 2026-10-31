import { randomUUID } from 'node:crypto';
import { expect, type APIRequestContext } from '@playwright/test';

export const bootstrapAdmin = { username: 'admin', password: 'admin' };
export const admin = { username: 'admin', password: 'end-to-end admin passphrase' };

export interface Account {
  id: string;
  username: string;
  password: string;
}

const bearer = (token: string) => ({ Authorization: `Bearer ${token}` });

export async function signIn(
  request: APIRequestContext,
  usernameOrEmail: string,
  password: string,
) {
  const response = await request.post('/api/v1/auth/login/mobile', {
    data: { usernameOrEmail, password },
  });
  expect(response.status(), await response.text()).toBe(200);
  return ((await response.json()) as { accessToken: string }).accessToken;
}

export async function changePassword(
  request: APIRequestContext,
  token: string,
  currentPassword: string,
  newPassword: string,
) {
  const response = await request.post('/api/v1/users/me/password', {
    headers: bearer(token),
    data: { currentPassword, newPassword },
  });
  expect(response.status(), await response.text()).toBe(204);
}

export async function createAccount(request: APIRequestContext): Promise<Account> {
  const adminToken = await signIn(request, admin.username, admin.password);
  const username = `user-${randomUUID().slice(0, 8)}`;
  const created = await request.post('/api/v1/users', {
    headers: bearer(adminToken),
    data: { username, email: `${username}@example.com` },
  });
  expect(created.status(), await created.text()).toBe(201);
  const { id, temporaryPassword } = (await created.json()) as {
    id: string;
    temporaryPassword: string;
  };
  const password = `${username} passphrase`;
  const token = await signIn(request, username, temporaryPassword);
  await changePassword(request, token, temporaryPassword, password);
  return { id, username, password };
}

export async function disable(request: APIRequestContext, account: Account) {
  const adminToken = await signIn(request, admin.username, admin.password);
  const response = await request.patch(`/api/v1/users/${account.id}`, {
    headers: bearer(adminToken),
    data: { isAdmin: null, isDisabled: true },
  });
  expect(response.status(), await response.text()).toBe(200);
}

async function failSignIn(request: APIRequestContext, account: Account) {
  const response = await request.post('/api/v1/auth/login/mobile', {
    data: { usernameOrEmail: account.username, password: 'not the password' },
  });
  expect(response.status()).toBe(401);
}

const pause = (seconds: number) => new Promise((resolve) => setTimeout(resolve, seconds * 1000));

// Failures 4, 5 and 6 lock for 2, 4 and 8 s; one during a lock is not counted.
export async function lockOut(request: APIRequestContext, account: Account) {
  for (let attempt = 0; attempt < 4; attempt++) await failSignIn(request, account);
  await pause(2.5);
  await failSignIn(request, account);
  await pause(4.5);
  await failSignIn(request, account);
}
