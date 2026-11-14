import { HttpResponse } from 'msw';
import { createOpenApiHttp } from 'openapi-msw';
import type { paths } from '../../api/generated/v1';

const http = createOpenApiHttp<paths>();

type Answer = Response | Promise<Response | undefined> | undefined;

export function proxyAnswers(
  received: string[] = [],
  answer: (url: string) => Answer = () => undefined,
) {
  return http.get('/api/v1/proxies/{assetId}/{type}', async ({ request, response }) => {
    const { pathname, search } = new URL(request.url);
    const url = pathname + search;
    received.push(url);
    return response.untyped(
      (await answer(url)) ?? new HttpResponse(new Uint8Array([1]), { status: 200 }),
    );
  });
}
