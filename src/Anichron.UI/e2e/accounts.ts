import { randomUUID } from 'node:crypto';
import type { components } from '../src/api/generated/v1';
import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const bootstrapAdmin = { username: 'admin', password: 'end-to-end bootstrap passphrase' };
export const admin = { username: 'admin', password: 'end-to-end admin passphrase' };
export const mediaUser = {
  username: 'mira',
  password: 'mira end-to-end passphrase',
  rootPath: '/data/originals/mira',
};

export interface Account {
  id: string;
  username: string;
  password: string;
}

type StorageConfig = components['schemas']['AdminStorageConfigResponse'];

type Credentials = Pick<Account, 'username' | 'password'>;

export const bearer = (token: string) => ({ Authorization: `Bearer ${token}` });

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

export async function fillInSignIn(page: Page, { username, password }: Credentials) {
  await page.getByLabel('Username or email').fill(username);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

export async function signInPage(page: Page, credentials: Credentials) {
  await page.goto('/login');
  await fillInSignIn(page, credentials);
  await expect(page.getByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();
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
  expect(response.status(), await response.text()).toBe(200);
}

export async function createAccount(
  request: APIRequestContext,
  { photos = true, keepTemporaryPassword = false } = {},
): Promise<Account> {
  const adminToken = await signIn(request, admin.username, admin.password);
  const username = `user-${randomUUID().slice(0, 8)}`;
  const { id, temporaryPassword } = await addUser(request, adminToken, username);
  if (photos) await addStorageConfig(request, adminToken, id, `/data/originals/${username}`);
  if (keepTemporaryPassword) return { id, username, password: temporaryPassword };
  const password = `${username} passphrase`;
  await replaceTemporaryPassword(request, username, temporaryPassword, password);
  return { id, username, password };
}

export async function createUser(
  request: APIRequestContext,
  { username, password, rootPath }: Credentials & { rootPath: string },
) {
  const adminToken = await signIn(request, admin.username, admin.password);
  const { id, temporaryPassword } = await addUser(request, adminToken, username);
  const storageConfigId = await addStorageConfig(request, adminToken, id, rootPath);
  await replaceTemporaryPassword(request, username, temporaryPassword, password);
  return { id, storageConfigId };
}

async function addUser(request: APIRequestContext, adminToken: string, username: string) {
  const created = await request.post('/api/v1/users', {
    headers: bearer(adminToken),
    data: { username, email: `${username}@example.com` },
  });
  expect(created.status(), await created.text()).toBe(201);
  return (await created.json()) as { id: string; temporaryPassword: string };
}

async function addStorageConfig(
  request: APIRequestContext,
  adminToken: string,
  userId: string,
  rootPath: string,
) {
  const storage = await request.post(`/api/v1/users/${userId}/storage-configs`, {
    headers: bearer(adminToken),
    data: { rootPath },
  });
  expect(storage.status(), await storage.text()).toBe(201);
  return ((await storage.json()) as StorageConfig).id;
}

async function replaceTemporaryPassword(
  request: APIRequestContext,
  username: string,
  temporaryPassword: string,
  password: string,
) {
  const token = await signIn(request, username, temporaryPassword);
  await changePassword(request, token, temporaryPassword, password);
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

export const pause = (seconds: number) =>
  new Promise((resolve) => setTimeout(resolve, seconds * 1000));

// Failures 4, 5 and 6 lock for 2, 4 and 8 s; one during a lock is not counted.
export async function lockOut(request: APIRequestContext, account: Account) {
  for (let attempt = 0; attempt < 4; attempt++) await failSignIn(request, account);
  await pause(2.5);
  await failSignIn(request, account);
  await pause(4.5);
  await failSignIn(request, account);
}
