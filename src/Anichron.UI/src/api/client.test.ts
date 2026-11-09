import { describe, expect, it } from 'vitest';
import { loginSucceeds } from '../test/handlers/auth';
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
