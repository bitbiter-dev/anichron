import { createOpenApiHttp } from 'openapi-msw';
import type { components, paths } from '../../api/generated/v1';

type Schemas = components['schemas'];

const http = createOpenApiHttp<paths>();

export interface Interacted {
  id: string;
  body: unknown;
}

type Answer = Response | Promise<Response> | undefined;

export function interactionAnswers(
  sent: Interacted[],
  answer: (interacted: Interacted) => Answer = () => undefined,
) {
  return http.patch(
    '/api/v1/assets/{assetId}/interaction',
    async ({ params, request, response }) => {
      const interacted = { id: params.assetId, body: await request.json() };
      sent.push(interacted);
      const failed = await answer(interacted);
      if (failed) return response.untyped(failed);
      return response(200).json({
        starred: false,
        liked: false,
        hidden: false,
        displayWeight: 1,
        ...(interacted.body as Partial<Schemas['AssetInteractionDto']>),
      });
    },
  );
}
