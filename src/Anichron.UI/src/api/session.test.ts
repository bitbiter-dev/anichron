import createClient from 'openapi-fetch';
import { describe, expect, it } from 'vitest';
import { loginSucceeds } from '../test/handlers/auth';
import { server } from '../test/server';
import { sharedTabs } from '../test/tabs';
import type { paths } from './generated/v1';
import { createSession } from './session';

describe('a session listener', () => {
  it('hears about a new session until it stops listening', async () => {
    server.use(loginSucceeds());
    const { session } = createSession(
      createClient<paths>({ baseUrl: location.origin }),
      sharedTabs().tab(),
    );
    const heard: string[] = [];
    session.onChange(() => heard.push('staying'));
    const stop = session.onChange(() => heard.push('stopped'));

    stop();
    await session.login({ usernameOrEmail: 'mira', password: 'hunter2' });

    expect(heard).toEqual(['staying']);
  });
});
