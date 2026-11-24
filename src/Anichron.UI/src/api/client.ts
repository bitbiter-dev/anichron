import createClient from 'openapi-fetch';
import { createAssets, type Assets } from './assets';
import { createBursts, type Bursts } from './bursts';
import { showsCompactPreviews, type Display } from './display';
import { createFlashbacks, type Flashbacks } from './flashbacks';
import type { paths } from './generated/v1';
import { createHidden, type Hidden } from './hidden';
import { createMedia, type Media, type ObjectUrls } from './media';
import { createShowMemories } from './memories';
import { createSession, type Session } from './session';
import { createSoftDeleted, type SoftDeleted } from './soft-deleted';
import type { Tabs } from './tabs';
import { createWorkItems, type WorkItems } from './work-items';

export interface ApiClient {
  session: Session;
  flashbacks: Flashbacks;
  assets: Assets;
  bursts: Bursts;
  hidden: Hidden;
  softDeleted: SoftDeleted;
  media: Media;
  workItems: WorkItems;
}

export function createApiClient(tabs: Tabs, display: Display, objectUrls?: ObjectUrls): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  const { session, authorized } = createSession(client, tabs);
  const show = createShowMemories(showsCompactPreviews(display));
  return {
    session,
    flashbacks: createFlashbacks(client, authorized, show),
    assets: createAssets(client, authorized),
    bursts: createBursts(client, authorized, show),
    hidden: createHidden(client, authorized, show),
    softDeleted: createSoftDeleted(client, authorized, show),
    media: createMedia(() => session.renewMedia(), objectUrls),
    workItems: createWorkItems(client, authorized),
  };
}
