import { createOpenApiHttp } from 'openapi-msw';
import type { components, paths } from '../../api/generated/v1';
import { memory } from './flashbacks';

type Schemas = components['schemas'];

const http = createOpenApiHttp<paths>();

type Page = Schemas['HiddenAssetsPageDto'];
type Answer = Page | Response | Promise<Page | Response>;

export function hiddenMemory(
  id: string,
  hiddenAt: Date,
  overrides: Partial<Schemas['AssetDto']> = {},
): Schemas['HiddenAssetDto'] {
  return {
    asset: memory(id, 2019, {
      month: 11,
      day: 12,
      interaction: { starred: false, liked: false, hidden: true, displayWeight: 1 },
      ...overrides,
    }),
    hiddenAt: hiddenAt.toISOString(),
  };
}

export function hiddenAnswers(answer: (after: string | null) => Answer) {
  return http.get('/api/v1/assets/hidden', async ({ request, response }) => {
    const page = await answer(new URL(request.url).searchParams.get('after'));
    return page instanceof Response ? response.untyped(page) : response(200).json(page);
  });
}
