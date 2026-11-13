import { createOpenApiHttp } from 'openapi-msw';
import type { components, paths } from '../../api/generated/v1';
import { problemResponse } from './auth';

type Schemas = components['schemas'];

const http = createOpenApiHttp<paths>();

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
      thumbnail: `proxies/${id}/thumbnail?v=0123456789abcdef`,
      preview: `proxies/${id}/preview?v=0123456789abcdef`,
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
