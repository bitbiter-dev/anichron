export interface Videos {
  keep(urls: readonly string[]): void;
  sourceOf(url: string): string;
  clear(): void;
}

export interface Media {
  prefetchPicture(url: string): void;
  videos(): Videos;
}

async function fetchAndDecode(url: string) {
  const response = await fetch(new URL(url, location.origin));
  if (!response.ok) return;
  await response.blob();
  const image = new Image();
  image.src = url;
  await image.decode();
}

interface Kept {
  source: string | null;
}

function createVideos(): Videos {
  const kept = new Map<string, Kept>();

  async function load(url: string, entry: Kept) {
    const response = await fetch(new URL(url, location.origin));
    if (!response.ok) return;
    const blob = await response.blob();
    if (kept.get(url) === entry) entry.source = URL.createObjectURL(blob);
  }

  function drop(url: string) {
    const source = kept.get(url)?.source;
    kept.delete(url);
    if (source) URL.revokeObjectURL(source);
  }

  return {
    keep(urls) {
      for (const url of kept.keys()) if (!urls.includes(url)) drop(url);
      for (const url of urls) {
        if (kept.has(url)) continue;
        const entry: Kept = { source: null };
        kept.set(url, entry);
        load(url, entry).catch(() => undefined);
      }
    },
    sourceOf: (url) => kept.get(url)?.source ?? url,
    clear() {
      for (const url of kept.keys()) drop(url);
    },
  };
}

export function createMedia(): Media {
  return {
    prefetchPicture(url) {
      fetchAndDecode(url).catch(() => undefined);
    },
    videos: createVideos,
  };
}
