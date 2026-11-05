import type { LoginRequest, LoginResponse } from './login-contract';
import { readProblem, unknownProblem, unreachableProblem, type Problem } from './problem';

export type LoginOutcome = { ok: true } | { ok: false; problem: Problem };

export interface ApiClient {
  login(request: LoginRequest): Promise<LoginOutcome>;
  isSignedIn(): boolean;
}

export function createApiClient(): ApiClient {
  let accessToken: string | undefined;
  return {
    async login(request) {
      const response = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
      }).catch(() => undefined);
      if (!response) return { ok: false, problem: unreachableProblem };
      if (!response.ok) return { ok: false, problem: await readProblem(response) };
      try {
        accessToken = ((await response.json()) as LoginResponse).accessToken;
        return { ok: true };
      } catch {
        return { ok: false, problem: unknownProblem };
      }
    },
    isSignedIn: () => accessToken !== undefined,
  };
}
