import type { Client } from 'openapi-fetch';
import type { paths } from './generated/v1';
import type { Memory, ShowMemories } from './memories';
import type { Authorized } from './session';

export interface YearGroup {
  year: number;
  memories: Memory[];
}

export interface Day {
  years: YearGroup[];
}

export interface Flashbacks {
  onThisDay(date: string, signal?: AbortSignal): Promise<Day>;
}

export function createFlashbacks(
  client: Client<paths>,
  authorized: Authorized,
  show: ShowMemories,
): Flashbacks {
  return {
    async onThisDay(date, signal) {
      const day = await authorized((init) =>
        client.GET('/api/v1/flashbacks/on-this-day', {
          ...init,
          signal,
          params: { query: { date } },
        }),
      );
      return show((memoryOf) => ({
        years: day.years.map(({ year, assets }) => ({ year, memories: assets.map(memoryOf) })),
      }));
    },
  };
}
