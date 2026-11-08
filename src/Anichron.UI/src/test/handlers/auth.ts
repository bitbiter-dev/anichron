import { createOpenApiHttp } from 'openapi-msw';
import type { components, paths } from '../../api/generated/v1';

type Schemas = components['schemas'];

const http = createOpenApiHttp<paths>();

export const miraSession: Schemas['SessionResponse'] = {
  accessToken: 'header.payload.signature',
  expiresIn: 900,
  user: {
    id: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
    username: 'mira',
    email: 'mira@example.com',
    isAdmin: false,
    mustChangePassword: false,
  },
};

const problemHeaders = { 'Content-Type': 'application/problem+json' };

export function problem<C extends string>(status: number, code: C, extra: object = {}) {
  return {
    status,
    detail: 'Server text that is never shown.',
    code,
    ...extra,
  };
}

export function loginSucceeds(
  onRequest: (request: Schemas['LoginRequest']) => void = () => undefined,
) {
  return http.post('/api/v1/auth/login', async ({ request, response }) => {
    if (request.headers.get('Content-Type') !== 'application/json')
      return response(415).json({ status: 415 }, { headers: problemHeaders });
    onRequest(await request.json());
    return response(200).json(miraSession);
  });
}

export function loginFails(status: number, body: object, headers: Record<string, string> = {}) {
  return http.post('/api/v1/auth/login', ({ response }) =>
    response.untyped(Response.json(body, { status, headers: { ...problemHeaders, ...headers } })),
  );
}

export function loginRespondsWith(respond: () => Response) {
  return http.post('/api/v1/auth/login', ({ response }) => response.untyped(respond()));
}

export function refreshSucceeds() {
  return http.post('/api/v1/auth/refresh', ({ response }) => response(200).json(miraSession));
}

export function refreshFails(code: Schemas['ErrorCode'] = 'refresh_token_required') {
  return http.post('/api/v1/auth/refresh', ({ response }) =>
    response(401).json(problem(401, code), { headers: problemHeaders }),
  );
}

export function refreshRespondsWith(respond: () => Response | Promise<Response>) {
  return http.post('/api/v1/auth/refresh', async ({ response }) =>
    response.untyped(await respond()),
  );
}

export function meAnswersFor(
  token: (bearer: string | null) => boolean = (bearer) =>
    bearer === `Bearer ${miraSession.accessToken}`,
  user: Schemas['UserProfileResponse'] = miraSession.user,
) {
  return http.get('/api/v1/users/me', ({ request, response }) =>
    token(request.headers.get('Authorization'))
      ? response(200).json(user)
      : response(401).json({ status: 401 }, { headers: problemHeaders }),
  );
}
