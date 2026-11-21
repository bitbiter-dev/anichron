import type { ObjectUrls } from '../api/media';

const freed: string[] = [];

export function keptSource(url: string) {
  return `memory:${url}`;
}

export function memoryUrls(): ObjectUrls {
  return {
    create: (_, url) => keptSource(url),
    free: (source) => {
      freed.push(source);
    },
    made: (source) => source.startsWith('memory:'),
  };
}

export function freedSources() {
  return [...freed];
}

export function forgetSources() {
  freed.length = 0;
}
