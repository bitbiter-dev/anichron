import { expect, it, vi } from 'vitest';
import { listen } from './listen';

it('passes each window event of its type to the handler until stopped', () => {
  const handler = vi.fn();
  const stop = listen('scroll', handler);

  window.dispatchEvent(new Event('resize'));
  window.dispatchEvent(new Event('scroll'));
  expect(handler).toHaveBeenCalledTimes(1);

  stop();
  window.dispatchEvent(new Event('scroll'));
  expect(handler).toHaveBeenCalledTimes(1);
});
