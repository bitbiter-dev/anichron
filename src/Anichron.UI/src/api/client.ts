import createClient from 'openapi-fetch';
import { createAssets, type Assets } from './assets';
import { createBursts, type Bursts } from './bursts';
import { showsCompactPreviews, type Display } from './display';
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

export function createApiClient(tabs: Tabs, display: Display, objectUrls?: ObjectUrls): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  const { session, authorized } = createSession(client, tabs);
  const compact = showsCompactPreviews(display);
  return {
    session,
    flashbacks: createFlashbacks(client, authorized, compact),
    assets: createAssets(client, authorized),
    bursts: createBursts(client, authorized, compact),
    media: createMedia(() => session.renewMedia(), objectUrls),
    workItems: createWorkItems(client, authorized),
  };
}
