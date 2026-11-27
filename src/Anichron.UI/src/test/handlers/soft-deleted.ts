import { createOpenApiHttp } from 'openapi-msw';
import type { components, paths } from '../../api/generated/v1';
import { proxyPath } from './flashbacks';

type Schemas = components['schemas'];

const http = createOpenApiHttp<paths>();

type Item = Schemas['SoftDeletedAssetDto'];
type Page = Omit<Schemas['SoftDeletedAssetsPageDto'], 'total'> & { total?: number };
type Answer = Page | Response | Promise<Page | Response>;

export function removedMemory(id: string, path: string, overrides: Partial<Item> = {}): Item {
  return {
    id,
    mediaType: 'Image',
    year: 2019,
    month: 11,
    day: 12,
    width: 4032,
    height: 3024,
    durationSeconds: null,
    proxies: {
      thumbnail: proxyPath(id, 'thumbnail'),
      preview: proxyPath(id, 'preview'),
      compactPreview: null,
      video: null,
    },
    blurhash: null,
    livePhotoVideoUrl: null,
    path,
    lastSeenOnNas: '2026-11-03T12:00:00Z',
    reason: 'Missing',
    ...overrides,
  };
}

export function softDeletedAnswers(answer: (after: string | null) => Answer) {
  return http.get('/api/v1/assets/soft-deleted', async ({ request, response }) => {
    const page = await answer(new URL(request.url).searchParams.get('after'));
    return page instanceof Response
      ? response.untyped(page)
      : response(200).json({ total: page.items.length, ...page });
  });
}

export function purgeAnswers(
  sent: string[][],
  answer: (ids: string[]) => string[] | Response | Promise<string[]> = (ids) => ids,
) {
  return http.post('/api/v1/assets/purges', async ({ request, response }) => {
    const { assetIds } = await request.json();
    sent.push(assetIds);
    const accepted = await answer(assetIds);
    return accepted instanceof Response
      ? response.untyped(accepted)
      : response(202).json({ accepted });
  });
}
