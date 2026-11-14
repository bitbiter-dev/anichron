export interface Media {
  prefetchPicture(url: string): void;
}

async function fetchAndDecode(url: string) {
  const response = await fetch(new URL(url, location.origin));
  if (!response.ok) return;
  await response.blob();
  const image = new Image();
  image.src = url;
  await image.decode();
}

export function createMedia(): Media {
  return {
    prefetchPicture(url) {
      fetchAndDecode(url).catch(() => undefined);
    },
  };
}
