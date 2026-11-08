import createClient from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import { toProblem, unknownProblem, unreachableProblem, type Problem } from './problem';

type Schemas = components['schemas'];
export type LoginRequest = Schemas['LoginRequest'];
export type UserProfile = Schemas['UserProfileResponse'];

export class SessionNotRenewed extends Error {}

export type Outcome = { ok: true } | { ok: false; problem: Problem };

interface Call<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

type Answer<T> = { status?: number } & ({ ok: true; data: T } | { ok: false; problem: Problem });

export interface ApiClient {
  login(request: LoginRequest): Promise<Outcome>;
  restore(): Promise<void>;
  isSignedIn(): boolean;
  me(): Promise<UserProfile>;
  setSessionEndedHandler(handler: () => void): void;
  watchVisibility(): () => void;
}

const renewAtShare = 0.8;
const firstBackoffSeconds = 5;
const maxBackoffSeconds = 300;

const backoffSeconds = (failures: number) =>
  Math.min(firstBackoffSeconds * 2 ** (failures - 1), maxBackoffSeconds);

async function send<T>(call: () => Promise<Call<T>>): Promise<Answer<T>> {
  try {
    const { data, error, response } = await call();
    return data === undefined
      ? { ok: false, status: response.status, problem: toProblem(error, response) }
      : { ok: true, status: response.status, data };
  } catch (thrown) {
    return {
      ok: false,
      problem: thrown instanceof SyntaxError ? unknownProblem : unreachableProblem,
    };
  }
}

export function createApiClient(): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  let accessToken = '';
  let renewAt = Infinity;
  let restored: Promise<void> | undefined;
  let refreshing: Promise<boolean> | undefined;
  let failures = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let sessionEnded = () => {};

  function wakeIn(milliseconds: number) {
    clearTimeout(timer);
    timer = setTimeout(() => void refresh(), milliseconds);
  }

  function open(session: Schemas['SessionResponse']) {
    const renewIn = Number(session.expiresIn) * 1000 * renewAtShare;
    accessToken = session.accessToken;
    renewAt = Date.now() + renewIn;
    failures = 0;
    wakeIn(renewIn);
  }

  function end() {
    clearTimeout(timer);
    accessToken = '';
    renewAt = Infinity;
    sessionEnded();
  }

  async function sendRefresh() {
    const answer = await send(() => client.POST('/api/v1/auth/refresh'));
    if (answer.ok) {
      open(answer.data);
      return true;
    }
    if (answer.status === 401) {
      end();
      return false;
    }
    if (accessToken) {
      failures++;
      wakeIn((answer.problem.retryAfterSeconds || backoffSeconds(failures)) * 1000);
    }
    return false;
  }

  function refresh() {
    refreshing ??= sendRefresh().finally(() => {
      refreshing = undefined;
    });
    return refreshing;
  }

  async function authorized<T>(
    call: (init: { headers: { Authorization: string } }) => Promise<Call<T>>,
  ) {
    const sentWith = accessToken;
    const withBearer = () =>
      send(() => call({ headers: { Authorization: `Bearer ${accessToken}` } }));
    let answer = await withBearer();
    if (answer.status === 401) {
      if (accessToken === sentWith && !(await refresh())) throw new SessionNotRenewed();
      answer = await withBearer();
      if (answer.status === 401) end();
    }
    if (!answer.ok) throw new Error(answer.problem.code);
    return answer.data;
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'visible' && Date.now() > renewAt) void refresh();
  }

  return {
    async login(body) {
      const answer = await send(() => client.POST('/api/v1/auth/login', { body }));
      if (!answer.ok) return answer;
      open(answer.data);
      return { ok: true };
    },
    restore() {
      restored ??= refresh().then(() => undefined);
      return restored;
    },
    isSignedIn: () => accessToken !== '',
    me: () => authorized((init) => client.GET('/api/v1/users/me', init)),
    setSessionEndedHandler(handler) {
      sessionEnded = handler;
    },
    watchVisibility() {
      document.addEventListener('visibilitychange', onVisibilityChange);
      return () => {
        document.removeEventListener('visibilitychange', onVisibilityChange);
      };
    },
  };
}
