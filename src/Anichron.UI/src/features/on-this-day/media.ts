export function decoded(image: HTMLImageElement, ready: () => void) {
  void image.decode().then(ready, ready);
}
