import createClient from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import { toProblem, unknownProblem, unreachableProblem, type Problem } from './problem';

type Schemas = components['schemas'];
export type LoginRequest = Schemas['LoginRequest'];

export type Outcome = { ok: true } | { ok: false; problem: Problem };

interface SessionCall {
  data?: Schemas['SessionResponse'];
  error?: unknown;
  response: Response;
}

export interface ApiClient {
  login(request: LoginRequest): Promise<Outcome>;
  restore(): Promise<void>;
  isSignedIn(): boolean;
}

export function createApiClient(): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  let session: Schemas['SessionResponse'] | undefined;
  let restored: Promise<void> | undefined;

  async function openSession(call: () => Promise<SessionCall>): Promise<Outcome> {
    try {
      const { data, error, response } = await call();
      if (!data) return { ok: false, problem: toProblem(error, response) };
      session = data;
      return { ok: true };
    } catch (thrown) {
      return {
        ok: false,
        problem: thrown instanceof SyntaxError ? unknownProblem : unreachableProblem,
      };
    }
  }

  return {
    login: (body) => openSession(() => client.POST('/api/v1/auth/login', { body })),
    restore() {
      restored ??= openSession(() => client.POST('/api/v1/auth/refresh')).then(() => undefined);
      return restored;
    },
    isSignedIn: () => session !== undefined,
  };
}
