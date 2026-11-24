import type { APIRequestContext } from '@playwright/test';
import { bearer } from './accounts';

interface WorkItem {
  kind: 'Crawl' | 'BurstDetection';
  storageConfigId: string | null;
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled';
  startedAt: string | null;
  finishedAt: string | null;
  result: Record<string, number> | null;
  error: string | null;
}

const waitSeconds = 300;
const logs = 'docker compose -f src/docker-compose.e2e.yml logs anichron-worker';

async function itemsOf(
  request: APIRequestContext,
  token: string,
  kind: WorkItem['kind'],
  storageConfigId: string,
) {
  const response = await request.get('/api/v1/admin/work-items', {
    headers: bearer(token),
    params: { kind, pageSize: 200 },
  });
  if (response.status() !== 200) throw new Error(await response.text());
  const { items } = (await response.json()) as { items: WorkItem[] };
  const ours = items.filter((item) => item.storageConfigId === storageConfigId);
  const failed = ours.find((item) => item.status === 'Failed' || item.status === 'Cancelled');
  if (failed) throw new Error(`${kind} ${failed.status}: ${failed.error ?? ''}. See ${logs}.`);
  return ours.filter((item) => item.status === 'Succeeded');
}

async function ingested(request: APIRequestContext, token: string, storageConfigId: string) {
  const [crawl] = await itemsOf(request, token, 'Crawl', storageConfigId);
  if (!crawl) return false;
  if (crawl.result?.failed !== 0)
    throw new Error(`The crawl failed some files: ${JSON.stringify(crawl.result)}. See ${logs}.`);
  const crawled = Date.parse(crawl.finishedAt ?? '');
  const bursts = (await itemsOf(request, token, 'BurstDetection', storageConfigId)).find(
    (item) => Date.parse(item.startedAt ?? '') >= crawled,
  );
  if (!bursts) return false;
  if (!bursts.result?.created)
    throw new Error(
      `Burst detection formed no burst: ${JSON.stringify(bursts.result)}. See ${logs}.`,
    );
  return true;
}

export async function waitForIngestion(
  request: APIRequestContext,
  token: string,
  storageConfigId: string,
) {
  const deadline = Date.now() + waitSeconds * 1000;
  while (!(await ingested(request, token, storageConfigId))) {
    if (Date.now() > deadline)
      throw new Error(
        `The media library was not ingested in ${String(waitSeconds)} s. See ${logs}.`,
      );
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
}
