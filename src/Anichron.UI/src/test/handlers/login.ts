// Hand-written until #347 generates the API types; delete together with src/api/login-contract.ts.
import { http, HttpResponse } from 'msw';
import type { LoginRequest, LoginResponse } from '../../api/login-contract';

const loginPath = '/api/v1/auth/login';

export function loginSucceeds(onRequest: (request: LoginRequest) => void = () => undefined) {
  return http.post<never, LoginRequest, LoginResponse | object>(loginPath, async ({ request }) => {
    if (request.headers.get('Content-Type') !== 'application/json')
      return HttpResponse.json(problem(415, 'unsupported_media_type'), { status: 415 });
    onRequest(await request.json());
    return HttpResponse.json({ accessToken: 'header.payload.signature' });
  });
}

export function loginFails(status: number, body: object, headers: Record<string, string> = {}) {
  return http.post(loginPath, () =>
    HttpResponse.json(body, {
      status,
      headers: { 'Content-Type': 'application/problem+json', ...headers },
    }),
  );
}

export function problem(status: number, code: string, extra: object = {}) {
  return {
    status,
    detail: 'Server text that is never shown.',
    code,
    ...extra,
  };
}

export function loginRespondsWith(response: () => Response) {
  return http.post(loginPath, response);
}
