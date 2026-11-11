import type { Client } from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import {
  ProblemError,
  toProblem,
  unknownProblem,
  unreachableProblem,
  type Problem,
} from './problem';
import type { TabMessage, Tabs } from './tabs';

type Schemas = components['schemas'];
export type LoginRequest = Schemas['LoginRequest'];
export type User = Schemas['UserProfileResponse'];

interface Call<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

type Answer<T> = { status?: number } & ({ ok: true; data: T } | { ok: false; problem: Problem });

export interface Session {
  restore(): Promise<void>;
  login(request: LoginRequest): Promise<void>;
  logout(): Promise<void>;
  user: () => User | undefined;
  isSignedIn: () => boolean;
  onChange: (listener: () => void) => () => void;
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

export function createSession(client: Client<paths>, tabs: Tabs): Session {
  let accessToken = '';
  let user: User | undefined;
  let renewAt = Infinity;
  let restored: Promise<void> | undefined;
  let refreshing: ReturnType<typeof sendRefresh> | undefined;
  let failures = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const listeners = new Set<() => void>();

  function changed() {
    for (const listener of listeners) listener();
  }

  function wakeIn(milliseconds: number) {
    clearTimeout(timer);
    timer = setTimeout(() => void refresh(), milliseconds);
  }

  function open(session: Schemas['SessionResponse']) {
    const renewIn = Number(session.expiresIn) * 1000 * renewAtShare;
    accessToken = session.accessToken;
    user = session.user;
    renewAt = Date.now() + renewIn;
    failures = 0;
    wakeIn(renewIn);
    changed();
  }

  function end() {
    clearTimeout(timer);
    accessToken = '';
    user = undefined;
    renewAt = Infinity;
    changed();
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

  function endOnServer() {
    return send(() =>
      tabs.exclusively(() =>
        client.POST('/api/v1/auth/logout', {
          headers: { Authorization: `Bearer ${accessToken}` },
        }),
      ),
    );
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
    restore() {
      restored ??= refresh().then((answer) => {
        if (answer.ok || answer.status === 401) return;
        restored = undefined;
        throw new ProblemError(answer.problem);
      });
      return restored;
    },
    async login(body) {
      const answer = await tabs.exclusively(() =>
        send(() => client.POST('/api/v1/auth/login', { body })),
      );
      if (!answer.ok) throw new ProblemError(answer.problem);
      open(answer.data);
      tabs.announce('signed-in');
    },
    async logout() {
      if ((await endOnServer()).status === 401 && (await refresh()).ok) await endOnServer();
      end();
      tabs.announce('signed-out');
    },
    user: () => user,
    isSignedIn: () => user !== undefined,
    onChange(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
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
