import createClient from 'openapi-fetch';
import type { paths } from './generated/v1';
import { createSession, type Session } from './session';
import type { Tabs } from './tabs';

export interface ApiClient {
  session: Session;
}

export function createApiClient(tabs: Tabs): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  return { session: createSession(client, tabs) };
}
