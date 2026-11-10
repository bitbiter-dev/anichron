import createClient from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import { toProblem, unknownProblem, unreachableProblem, type Problem } from './problem';
import type { TabMessage, Tabs } from './tabs';

type Schemas = components['schemas'];
export type LoginRequest = Schemas['LoginRequest'];
export type UserProfile = Schemas['UserProfileResponse'];

export class SessionNotRenewed extends Error {}

export class SessionNotRestored extends Error {
  readonly problem: Problem;

  constructor(problem: Problem) {
    super(problem.code);
    this.problem = problem;
  }
}

export type Outcome = { ok: true } | { ok: false; problem: Problem };

interface Call<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

type Answer<T> = { status?: number } & ({ ok: true; data: T } | { ok: false; problem: Problem });

export interface ApiClient {
  login(request: LoginRequest): Promise<Outcome>;
  logout(): Promise<void>;
  restore(): Promise<void>;
  isSignedIn: () => boolean;
  me(): Promise<UserProfile>;
  onSessionChange: (listener: () => void) => () => void;
  watch(): () => void;
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

const ignore = () => undefined;

export function createApiClient(tabs: Tabs): ApiClient {
  const client = createClient<paths>({ baseUrl: location.origin });
  let accessToken = '';
  let renewAt = Infinity;
  let restored: Promise<void> | undefined;
  let refreshing: ReturnType<typeof sendRefresh> | undefined;
  let failures = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const sessionListeners = new Set<() => void>();

  function sessionChanged() {
    for (const listener of sessionListeners) listener();
  }

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
    sessionChanged();
  }

  function end() {
    clearTimeout(timer);
    accessToken = '';
    renewAt = Infinity;
    sessionChanged();
  }

  async function sendRefresh() {
    const answer = await send(() => client.POST('/api/v1/auth/refresh'));
    if (answer.ok) open(answer.data);
    else if (answer.status === 401) end();
    else if (accessToken) {
      failures++;
      wakeIn((answer.problem.retryAfterSeconds || backoffSeconds(failures)) * 1000);
    }
    return answer;
  }

  function refresh() {
    refreshing ??= tabs.exclusively(sendRefresh).finally(() => {
      refreshing = undefined;
    });
    return refreshing;
  }

  async function withSession<T>(
    call: (init: { headers: { Authorization: string } }) => Promise<Call<T>>,
  ) {
    const sentWith = accessToken;
    const withBearer = () =>
      send(() => call({ headers: { Authorization: `Bearer ${accessToken}` } }));
    let answer = await withBearer();
    if (answer.status === 401) {
      if (accessToken === sentWith && !(await refresh()).ok) throw new SessionNotRenewed();
      answer = await withBearer();
      if (answer.status === 401) end();
    }
    return answer;
  }

  async function authorized<T>(
    call: (init: { headers: { Authorization: string } }) => Promise<Call<T>>,
  ) {
    const answer = await withSession(call);
    if (!answer.ok) throw new Error(answer.problem.code);
    return answer.data;
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'visible' && Date.now() > renewAt) void refresh();
  }

  const onTabMessage: Record<TabMessage, () => void> = {
    'signed-in'() {
      restored = undefined;
      end();
    },
    'signed-out': end,
  };

  return {
    async login(body) {
      const answer = await tabs.exclusively(() =>
        send(() => client.POST('/api/v1/auth/login', { body })),
      );
      if (!answer.ok) return answer;
      open(answer.data);
      tabs.announce('signed-in');
      return { ok: true };
    },
    async logout() {
      await withSession((init) =>
        tabs.exclusively(() => client.POST('/api/v1/auth/logout', init)),
      ).catch(ignore);
      end();
      tabs.announce('signed-out');
    },
    restore() {
      restored ??= refresh().then((answer) => {
        if (answer.ok || answer.status === 401) return;
        restored = undefined;
        throw new SessionNotRestored(answer.problem);
      });
      return restored;
    },
    isSignedIn: () => accessToken !== '',
    me: () => authorized((init) => client.GET('/api/v1/users/me', init)),
    onSessionChange(listener) {
      sessionListeners.add(listener);
      return () => sessionListeners.delete(listener);
    },
    watch() {
      document.addEventListener('visibilitychange', onVisibilityChange);
      const stopListening = tabs.listen((message) => {
        onTabMessage[message]();
      });
      return () => {
        document.removeEventListener('visibilitychange', onVisibilityChange);
        stopListening();
      };
    },
  };
}
