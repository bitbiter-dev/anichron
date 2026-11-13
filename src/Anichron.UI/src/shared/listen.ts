export function listen(target: EventTarget, type: string, handler: (event: Event) => void) {
  target.addEventListener(type, handler);
  return () => {
    target.removeEventListener(type, handler);
  };
}
