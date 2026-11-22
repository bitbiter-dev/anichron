export function listen(target: EventTarget, type: string, handler: (event: Event) => void) {
  target.addEventListener(type, handler);
  return () => {
    target.removeEventListener(type, handler);
  };
}

export function isVisible() {
  return document.visibilityState === 'visible';
}

export function onVisibilityChange(changed: () => void) {
  return listen(document, 'visibilitychange', changed);
}

export function onVisible(handler: () => void) {
  return onVisibilityChange(() => {
    if (isVisible()) handler();
  });
}

export function createChanges() {
  const changes = new EventTarget();
  return {
    notify: () => {
      changes.dispatchEvent(new Event('change'));
    },
    subscribe: (listener: () => void) => listen(changes, 'change', listener),
  };
}
