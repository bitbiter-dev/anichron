import createClient from 'openapi-fetch';
import { createFlashbacks, type Flashbacks } from './flashbacks';
import type { paths } from './generated/v1';
import { createSession, type Session } from './session';
import type { Tabs } from './tabs';
import { createWorkItems, type WorkItems } from './work-items';

export interface ApiClient {
  session: Session;
  flashbacks: Flashbacks;
  workItems: WorkItems;
}

export function createApiClient(tabs: Tabs): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  const { session, authorized } = createSession(client, tabs);
  return {
    session,
    flashbacks: createFlashbacks(client, authorized),
    workItems: createWorkItems(client, authorized),
  };
}
