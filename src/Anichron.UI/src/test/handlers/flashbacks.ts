import { createOpenApiHttp } from 'openapi-msw';
import { mostRecentDays } from '../../api/generated/limits';
import type { components, paths } from '../../api/generated/v1';
import { problemResponse } from './auth';

type Schemas = components['schemas'];

const http = createOpenApiHttp<paths>();

type ProxyType = 'thumbnail' | 'preview' | 'compact-preview' | 'video';

export function proxyPath(id: string, type: ProxyType, version = '0123456789abcdef') {
  return `proxies/${id}/${type}?v=${version}`;
}

export function proxyUrl(id: string, type: ProxyType, version?: string) {
  return `/api/v1/${proxyPath(id, type, version)}`;
}

export function memory(id: string, year: number, overrides: Partial<Schemas['AssetDto']> = {}) {
  return {
    id,
    mediaType: 'Image',
    year,
    month: 11,
    day: 11,
    width: 4032,
    height: 3024,
    durationSeconds: null,
    proxies: {
      thumbnail: proxyPath(id, 'thumbnail'),
      preview: proxyPath(id, 'preview'),
      compactPreview: null,
      video: null,
    },
    blurhash: null,
    livePhotoVideoUrl: null,
    interaction: { starred: false, liked: false, hidden: false, displayWeight: 1 },
    burst: null,
    ...overrides,
  } satisfies Schemas['AssetDto'];
}

export const yearGroup = (year: number, ...assets: Schemas['AssetDto'][]) => ({ year, assets });

type Years = Schemas['FlashbackYearDto'][];
type Answer = Years | Response | Promise<Years | Response>;

export function onThisDayAnswers(answer: (date: string, bearer: string | null) => Answer) {
  return http.get('/api/v1/flashbacks/on-this-day', async ({ request, response }) => {
    const date = new URL(request.url).searchParams.get('date') ?? '';
    const years = await answer(date, request.headers.get('Authorization'));
    return years instanceof Response
      ? response.untyped(years)
      : response(200).json({ date, years });
  });
}

const phoneToday = () => new Date().toLocaleDateString('en-CA');

export function todayAnd(days: string[], answer: (date: string, bearer: string | null) => Answer) {
  return (date: string, bearer: string | null) =>
    date === phoneToday() || days.includes(date) ? answer(date, bearer) : [];
}

export const nothingOnThisDay = () => onThisDayAnswers(() => []);

export interface RecentWindow {
  before: string;
  since: string | null;
  limit: number;
}

type RecentDays = Schemas['RecentDayDto'][];
type RecentAnswer = RecentDays | Response | Promise<RecentDays | Response>;

export function recentDaysAnswers(
  answer: (window: RecentWindow, bearer: string | null) => RecentAnswer,
) {
  return http.get('/api/v1/flashbacks/days', async ({ request, response }) => {
    const query = new URL(request.url).searchParams;
    const limit = Number(query.get('limit'));
    if (!Number.isInteger(limit) || limit < 1 || limit > mostRecentDays)
      return response.untyped(problemResponse(400, 'invalid_limit'));
    const days = await answer(
      {
        before: query.get('before') ?? '',
        since: query.get('since'),
        limit,
      },
      request.headers.get('Authorization'),
    );
    return days instanceof Response ? response.untyped(days) : response(200).json({ days });
  });
}

export const noRecentDays = () => recentDaysAnswers(() => []);

const dayBefore = (date: string) => {
  const day = new Date(`${date}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() - 1);
  return day.toISOString().slice(0, 10);
};

export function recentDaysOf(yearsOn: (date: string) => Years) {
  return ({ before, since, limit }: RecentWindow): RecentDays => {
    let yearBack = before;
    for (let day = 0; day < mostRecentDays; day++) yearBack = dayBefore(yearBack);
    const start = since !== null && since > yearBack ? since : yearBack;
    const days: RecentDays = [];
    for (
      let date = dayBefore(before);
      date >= start && days.length < limit;
      date = dayBefore(date)
    ) {
      const newest = yearsOn(date)[0];
      const memory = newest?.assets[0];
      if (newest && memory) days.push({ date, year: newest.year, memory });
    }
    return days;
  };
}

const yearOfUser = new Map([
  ['mira', 2023],
  ['noor', 2021],
  ['alice', 2019],
]);

const usernameOf = (bearer: string | null) => bearer?.replace(/^Bearer (token-)?/, '') ?? '';

const userYear = (bearer: string | null) => yearOfUser.get(usernameOf(bearer)) ?? 2023;

const memoryIdOf = (year: number) => `0199a1b2-c3d4-7e5f-8a9b-00000000${String(year)}`;

const memoriesIn = (year: number) => [yearGroup(year, memory(memoryIdOf(year), year))];

export const everyoneHasMemories = () =>
  onThisDayAnswers((_, bearer) => memoriesIn(userYear(bearer)));

export function hold() {
  let release: () => void = () => undefined;
  const until = new Promise<void>((resolve) => {
    release = resolve;
  });
  return { until, release };
}

export function eachSeesTheirOwn(answered: string[], holds = new Map<string, Promise<void>>()) {
  return onThisDayAnswers(async (_, bearer) => {
    const username = usernameOf(bearer);
    const year = yearOfUser.get(username);
    if (year === undefined) return problemResponse(401, 'unauthorized');
    await holds.get(username);
    answered.push(username);
    return memoriesIn(year);
  });
}

export const memoriesOf = (username: string) => ({
  name: new RegExp(` · ${String(yearOfUser.get(username))}$`),
});

export const assetIdOf = (username: string) => memoryIdOf(yearOfUser.get(username) ?? 0);
