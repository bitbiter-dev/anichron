import createClient from 'openapi-fetch';
import { createAssets, type Assets } from './assets';
import { createBursts, type Bursts } from './bursts';
import { createFlashbacks, type Flashbacks } from './flashbacks';
import type { paths } from './generated/v1';
import { createMedia, type Media, type ObjectUrls } from './media';
import { createSession, type Session } from './session';
import type { Tabs } from './tabs';
import { createWorkItems, type WorkItems } from './work-items';

export interface ApiClient {
  session: Session;
  flashbacks: Flashbacks;
  assets: Assets;
  bursts: Bursts;
  media: Media;
  workItems: WorkItems;
}

export function createApiClient(tabs: Tabs, objectUrls?: ObjectUrls): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  const { session, authorized } = createSession(client, tabs);
  return {
    session,
    flashbacks: createFlashbacks(client, authorized),
    assets: createAssets(client, authorized),
    bursts: createBursts(client, authorized),
    media: createMedia(() => session.renewMedia(), objectUrls),
    workItems: createWorkItems(client, authorized),
  };
}
