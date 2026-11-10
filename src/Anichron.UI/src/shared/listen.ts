export function listen(target: EventTarget, type: string, handler: () => void) {
  target.addEventListener(type, handler);
  return () => {
    target.removeEventListener(type, handler);
  };
}
