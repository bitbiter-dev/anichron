const held = new Map<string, Promise<boolean>>();
const asked: string[] = [];

export function holdPicture(url: string) {
  let answer: (loaded: boolean) => void = () => undefined;
  held.set(
    url,
    new Promise<boolean>((resolve) => {
      answer = resolve;
    }),
  );
  return {
    load: () => {
      answer(true);
    },
    fail: () => {
      answer(false);
    },
  };
}

export function loadImage(url: string) {
  asked.push(url);
  return held.get(url) ?? Promise.resolve(true);
}

export function askedPictures() {
  return [...asked];
}

export function forgetPictures() {
  held.clear();
  asked.length = 0;
}
