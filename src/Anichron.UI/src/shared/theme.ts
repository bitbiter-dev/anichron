import { listen } from './listen';

export const themes = ['system', 'light', 'dark'] as const;
export type Theme = (typeof themes)[number];
export type ThemeChoice = ReturnType<typeof createThemeChoice>;

const key = 'anichron.theme';
export const themeColour = 'meta[name="theme-color"]';

export function storedTheme(): Theme {
  try {
    const stored = localStorage.getItem(key);
    return themes.find((theme) => theme === stored) ?? 'system';
  } catch {
    return 'system';
  }
}

function store(theme: Theme) {
  try {
    localStorage.setItem(key, theme);
  } catch {
    // Storage unavailable.
  }
}

export function applyTheme(theme: Theme) {
  document.querySelector(`${themeColour}:not([media])`)?.remove();
  if (theme === 'system') {
    document.documentElement.removeAttribute('data-theme');
    return;
  }
  document.documentElement.setAttribute('data-theme', theme);
  for (const scheme of document.querySelectorAll(
    `${themeColour}[media="(prefers-color-scheme: ${theme})"]`,
  )) {
    const chosen = scheme.cloneNode() as Element;
    chosen.removeAttribute('media');
    document.head.prepend(chosen);
  }
}

export function createThemeChoice() {
  let current = storedTheme();
  const changes = new EventTarget();
  const change = (theme: Theme) => {
    current = theme;
    applyTheme(theme);
    changes.dispatchEvent(new Event('change'));
  };
  return {
    get: () => current,
    choose: (theme: Theme) => {
      store(theme);
      change(theme);
    },
    subscribe: (listener: () => void) => listen(changes, 'change', listener),
    watch: () => {
      const follow = () => {
        change(storedTheme());
      };
      follow();
      return listen(window, 'storage', follow);
    },
  };
}
