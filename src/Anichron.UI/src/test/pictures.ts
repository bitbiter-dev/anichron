const held = new Map<string, Promise<boolean>>();

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
  return held.get(url) ?? Promise.resolve(true);
}

export function forgetPictures() {
  held.clear();
}
