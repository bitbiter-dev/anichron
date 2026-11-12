import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, expect, vi } from 'vitest';
import '@testing-library/jest-dom/vitest';
import * as matchers from 'vitest-axe/matchers';
import { addThemeColourTags, removeThemeColourTags } from './index-head';
import { resumeMotionFrames } from './motion';
import { settleQueries } from './query-clients';
import { server } from './server';

expect.extend(matchers);

// Testing Library advances fake timers only when it finds a `jest` global.
Object.assign(globalThis, { jest: { advanceTimersByTime: vi.advanceTimersByTime.bind(vi) } });

beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' });
});

beforeEach(() => {
  vi.useFakeTimers();
  resumeMotionFrames();
  addThemeColourTags();
});

afterEach(async () => {
  await settleQueries();
  cleanup();
  document.documentElement.removeAttribute('lang');
  document.documentElement.removeAttribute('data-theme');
  removeThemeColourTags();
  localStorage.clear();
  server.resetHandlers();
  vi.useRealTimers();
  Reflect.deleteProperty(document, 'visibilityState');
  Reflect.deleteProperty(document.documentElement, 'scrollHeight');
  Object.defineProperty(window, 'scrollY', { value: 0, configurable: true });
});

afterAll(() => {
  server.close();
});

// jsdom decodes no images; a test fires `load` or `error` on the <img> itself.
Object.defineProperty(HTMLImageElement.prototype, 'decode', {
  value: () => Promise.resolve(),
  configurable: true,
  writable: true,
});

// jsdom has no scrolling; the router restores the scroll position on every navigation.
Object.defineProperty(window, 'scrollTo', { value: () => undefined });
