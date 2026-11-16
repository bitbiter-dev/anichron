import { createOpenApiHttp } from 'openapi-msw';
import type { components, paths } from '../../api/generated/v1';

type Schemas = components['schemas'];

const http = createOpenApiHttp<paths>();

type Frames = Schemas['AssetDto'][];
type Answer = Frames | Response | Promise<Frames | Response>;

export function burstAnswers(answer: (burstId: string) => Answer) {
  return http.get('/api/v1/bursts/{burstId}/assets', async ({ params, response }) => {
    const frames = await answer(params.burstId);
    return frames instanceof Response ? response.untyped(frames) : response(200).json(frames);
  });
}
