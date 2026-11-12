import createClient from 'openapi-fetch';
import { createAssets, type Assets } from './assets';
import { createFlashbacks, type Flashbacks } from './flashbacks';
import type { paths } from './generated/v1';
import { createSession, type Session } from './session';
import type { Tabs } from './tabs';

export interface ApiClient {
  session: Session;
  flashbacks: Flashbacks;
  assets: Assets;
}

export function createApiClient(tabs: Tabs): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  const { session, authorized } = createSession(client, tabs);
  return {
    session,
    flashbacks: createFlashbacks(client, authorized),
    assets: createAssets(client, authorized),
  };
}
