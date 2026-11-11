import { HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { loginRespondsWith, loginSucceeds } from '../test/handlers/auth';
import { server } from '../test/server';
import { sharedTabs } from '../test/tabs';
import { createApiClient } from './client';

describe('a session listener', () => {
  it('hears about a new session until it stops listening', async () => {
    server.use(loginSucceeds());
    const api = createApiClient(sharedTabs().tab());
    const heard: string[] = [];
    api.onSessionChange(() => heard.push('staying'));
    const stop = api.onSessionChange(() => heard.push('stopped'));

    stop();
    await api.login({ usernameOrEmail: 'mira', password: 'hunter2' });

    expect(heard).toEqual(['staying']);
  });
});

describe('a failed call', () => {
  it.each([
    [
      'a body that is not JSON',
      () => new HttpResponse('<html>Bad gateway</html>', { status: 502 }),
    ],
    ['a success that is not JSON', () => new HttpResponse('<html>Welcome</html>', { status: 200 })],
    [
      'a body that is not a problem',
      () => HttpResponse.json(['invalid_credentials'], { status: 401 }),
    ],
  ])('answers %s with a problem that has no code and no fields', async (_, response) => {
    server.use(loginRespondsWith(response));
    const api = createApiClient(sharedTabs().tab());

    const outcome = await api.login({ usernameOrEmail: 'mira', password: 'hunter2' });

    expect(outcome.ok).toBe(false);
    expect(!outcome.ok && outcome.problem).toEqual({
      code: undefined,
      fields: {},
      retryAfterSeconds: 0,
    });
  });
});
