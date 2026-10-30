import type { LoginRequest, LoginResponse } from './login-contract';
import { readProblem, unknownProblem, type Problem } from './problem';

export type LoginOutcome = { ok: true } | { ok: false; problem: Problem };

export interface ApiClient {
  login(request: LoginRequest): Promise<LoginOutcome>;
  isSignedIn(): boolean;
}

export function createApiClient(): ApiClient {
  let accessToken: string | undefined;
  return {
    async login(request) {
      try {
        const response = await fetch('/api/v1/auth/login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(request),
        });
        if (!response.ok) return { ok: false, problem: await readProblem(response) };
        accessToken = ((await response.json()) as LoginResponse).accessToken;
        return { ok: true };
      } catch {
        return { ok: false, problem: unknownProblem };
      }
    },
    isSignedIn: () => accessToken !== undefined,
  };
}
