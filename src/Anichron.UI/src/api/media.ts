export interface Videos {
  keep(urls: readonly string[]): void;
  sourceOf(url: string): string;
  clear(): void;
}

export interface Media {
  loadPicture(url: string): Promise<boolean>;
  loadImage(url: string): Promise<boolean>;
  videos(): Videos;
}

async function decodeImage(url: string) {
  const image = new Image();
  image.src = url;
  await image.decode();
  return true;
}

async function fetchAndDecode(url: string) {
  const response = await fetch(new URL(url, location.origin));
  if (!response.ok) return false;
  await response.blob();
  const image = new Image();
  image.src = url;
  await image.decode();
  return true;
}

interface Kept {
  source?: string;
}

function createVideos(): Videos {
  const kept = new Map<string, Kept>();
  const notKept: Kept = {};

  async function load(url: string, entry: Kept) {
    const response = await fetch(new URL(url, location.origin));
    if (!response.ok) return;
    const blob = await response.blob();
    if (kept.get(url) === entry) entry.source = URL.createObjectURL(blob);
  }

  function drop(url: string, { source }: Kept) {
    kept.delete(url);
    if (source) URL.revokeObjectURL(source);
  }

  return {
    keep(urls) {
      for (const [url, entry] of kept) if (!urls.includes(url)) drop(url, entry);
      for (const url of urls) {
        if (kept.has(url)) continue;
        const entry: Kept = {};
        kept.set(url, entry);
        load(url, entry).catch(() => undefined);
      }
    },
    sourceOf: (url) => (kept.get(url) ?? notKept).source ?? url,
    clear() {
      for (const [url, entry] of kept) drop(url, entry);
    },
  };
}

export function createMedia(): Media {
  return {
    loadPicture: (url) => fetchAndDecode(url).catch(() => false),
    loadImage: (url) => decodeImage(url).catch(() => false),
    videos: createVideos,
  };
}
