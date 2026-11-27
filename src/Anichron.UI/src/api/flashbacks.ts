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

export interface RecentDay {
  date: string;
  year: number;
  memory: Memory;
}

export interface RecentWindow {
  before: string;
  since?: string;
  limit: number;
}

export interface Flashbacks {
  onThisDay(date: string, signal?: AbortSignal): Promise<Day>;
  recentDays(window: RecentWindow, signal?: AbortSignal): Promise<RecentDay[]>;
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
    async recentDays({ before, since, limit }, signal) {
      const recent = await authorized((init) =>
        client.GET('/api/v1/flashbacks/days', {
          ...init,
          signal,
          params: { query: { before, since, limit: String(limit) } },
        }),
      );
      return show((memoryOf) =>
        recent.days.map(({ date, year, memory }) => ({ date, year, memory: memoryOf(memory) })),
      );
    },
  };
}
