export function listen<K extends keyof WindowEventMap>(
  type: K,
  handler: (event: WindowEventMap[K]) => void,
) {
  window.addEventListener(type, handler);
  return () => {
    window.removeEventListener(type, handler);
  };
}
