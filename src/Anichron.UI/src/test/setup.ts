import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, expect, vi } from 'vitest';
import '@testing-library/jest-dom/vitest';
import * as matchers from 'vitest-axe/matchers';
import { server } from './server';

expect.extend(matchers);

// Testing Library advances fake timers only when it finds a `jest` global.
Object.assign(globalThis, { jest: { advanceTimersByTime: vi.advanceTimersByTime.bind(vi) } });

beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' });
});

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  vi.useRealTimers();
});

afterAll(() => {
  server.close();
});

// jsdom has no scrolling; the router restores the scroll position on every navigation.
Object.defineProperty(window, 'scrollTo', { value: () => undefined });
